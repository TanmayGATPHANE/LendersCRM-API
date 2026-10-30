using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IGroupService
{
    Task<GroupItem> GetAsync(int groupId);
    Task MoveCustomerAsync(int groupId, int customerId);
    Task<ReleaseResult> ReleaseAsync(int groupId, ReleaseRequest req);
}

/** Operations on a whole collateral group: inspect it, move it (with its
    payments) to another customer, release it (plan §4.2, §4.3, §6). */
public sealed class GroupService(
    IEntryRepository entries, IPaymentRepository payments, ICustomerRepository customers,
    ICashLedgerRepository cash) : IGroupService
{
    public async Task<GroupItem> GetAsync(int groupId)
    {
        var groupEntries = await entries.ListForGroupAsync(groupId);
        if (groupEntries.Count == 0) throw ApiException.NotFound("Collateral group not found");
        var groupPayments = await payments.ListForGroupAsync(groupId);

        // same mapped shape as the entries list (paid + interest computed)
        var mapped = await EntryServiceListMapper(groupEntries, groupPayments);

        var c = groupEntries[0].Customer
            ?? await customers.FindByIdAsync(groupEntries[0].CustomerId)
            ?? throw ApiException.NotFound("Customer not found");
        var primary = groupEntries.OrderBy(e => (e.EntryDate, e.Id)).First();

        return new GroupItem(
            GroupId: groupId,
            Item: primary.ItemDescription,
            Status: groupEntries.Any(e => e.Status == "Active") ? "Active" : "Released",
            Customer: new CustomerDetail(c.Id, c.Name, c.Phone, c.Address),
            Entries: mapped,
            Payments: groupPayments.Select(PaymentMapper.ToItem).ToList());
    }

    public async Task MoveCustomerAsync(int groupId, int customerId)
    {
        var groupEntries = await entries.ListForGroupAsync(groupId, tracked: true);
        if (groupEntries.Count == 0) throw ApiException.NotFound("Collateral group not found");
        if (groupEntries.Any(e => e.Status == "Released"))
            throw ApiException.Conflict("Released collateral history cannot be moved");
        if (!await customers.ExistsAsync(customerId))
            throw ApiException.NotFound("Customer not found");

        groupEntries.ForEach(e => e.CustomerId = customerId);
        await entries.SaveAsync(); // lends + payments travel together
    }

    public async Task<ReleaseResult> ReleaseAsync(int groupId, ReleaseRequest req)
    {
        var groupEntries = await entries.ListForGroupAsync(groupId, tracked: true);
        if (groupEntries.Count == 0) throw ApiException.NotFound("Collateral group not found");
        var active = groupEntries.Where(e => e.Status == "Active").ToList();
        if (active.Count == 0) throw ApiException.Conflict("This collateral is already released");

        // Interest accrues — and freezes — on what is actually still out:
        // partials reduce the principal FIFO before anything else.
        var paid = Interest.AllocatePaid(groupEntries,
            (await payments.ListForGroupAsync(groupId)).Sum(p => p.Amount));

        // Each sub-lend freezes its own interest from its own lend date;
        // when the counter books a different total, it is spread across
        // the lends proportionally to what had accrued (§4.2).
        var accrued = active.ToDictionary(
            e => e.Id,
            e => Interest.Accrued(e.AmountLend - paid.GetValueOrDefault(e.Id), e.InterestRate, e.EntryDate, req.DateReturned));
        var due = accrued.Values.Sum();
        var books = req.InterestCollected ?? due; // null = collect exactly what is due
        var scale = due > 0m && books != due ? books / due : 1m;

        var now = DateTime.UtcNow;
        foreach (var e in active)
        {
            var frozen = Math.Round(accrued[e.Id] * scale, MidpointRounding.AwayFromZero);
            e.Status = "Released";
            e.ReleasedDate = req.DateReturned;
            e.InterestCollected = frozen;
            e.AmountReturned = e.AmountLend - paid.GetValueOrDefault(e.Id) + frozen;
            e.Notes = string.IsNullOrWhiteSpace(req.Notes) ? e.Notes : req.Notes;
            e.ModifiedAt = now;
        }

        // TotalCollected — the actual cash received — rides on the group's
        // primary (earliest) entry for the books (§4.2).
        var primary = active.MinBy(e => (e.EntryDate, e.Id))!;
        primary.TotalCollected = req.TotalCollected ?? 0m;

        // The Total Cash Available table (§3.5): when released, TWO entries
        // happen — principal amount and interest — dated the release day.
        var principalBack = active.Sum(e => (e.AmountReturned ?? 0m) - (e.InterestCollected ?? 0m));
        var interestBack = active.Sum(e => e.InterestCollected ?? 0m);
        await cash.AddAsync(new CashLedger
        {
            LedgerDate = req.DateReturned, Amount = principalBack,
            Source = "ReleasePrincipal", GroupId = groupId,
        });
        await cash.AddAsync(new CashLedger
        {
            LedgerDate = req.DateReturned, Amount = interestBack,
            Source = "ReleaseInterest", GroupId = groupId,
            Notes = primary.Notes,
        });

        // One SaveChanges ⇒ one DB transaction: the status flips, the
        // frozen interest AND the two cash-ledger rows commit together.
        await entries.SaveAsync();
        return new ReleaseResult(groupId, primary.Id, primary.TotalCollected, active.Count);
    }

    /** Shared FIFO paid mapping (same rule as the flat entries list). */
    private async Task<List<EntryItem>> EntryServiceListMapper(List<Entry> groupEntries, List<PartialReturn> groupPayments)
    {
        var paidMap = Interest.AllocatePaid(groupEntries, groupPayments.Sum(p => p.Amount));
        var today = DateOnly.FromDateTime(DateTime.Today);
        return groupEntries.Select(e => EntryMapper.ToItem(e, paidMap.GetValueOrDefault(e.Id), today)).ToList();
    }
}