using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IEntryService
{
    Task<List<EntryItem>> ListAsync(string? status, string? search);
    Task<(int Id, int GroupId)> AddAsync(NewEntryRequest req, Actor actor);
    Task<(int Id, int GroupId)> UpdateAsync(int id, UpdateEntryRequest req, Actor actor);
}

/** Lend entries (plan ±§4.1, ±§4.3, ±§4.5): list with computed interest,
    add (new collateral or sub-lend), edit while active. */
public sealed class EntryService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash,
    ICustomerRepository customers, IActivityRepository activity) : IEntryService
{
    public async Task<List<EntryItem>> ListAsync(string? status, string? search)
    {
        var list = await entries.ListAsync(status, search);
        return await MapWithFifoPaid(list);
    }

    public async Task<(int Id, int GroupId)> AddAsync(NewEntryRequest req, Actor actor)
    {
        if (req.Amount <= 0) throw ApiException.BadRequest("Amount must be greater than 0");
        var item = (req.Item ?? "").Trim();
        if (item.Length == 0) throw ApiException.BadRequest("Item description is required");
        if (!await entries.CustomerExistsAsync(req.CustomerId))
            throw ApiException.BadRequest("Customer not found");
        var owner = await customers.FindByIdAsync(req.CustomerId);

        var entry = new Entry
        {
            CustomerId = req.CustomerId,
            EntryDate = req.Date,
            ItemDescription = item,
            AmountLend = req.Amount,
            InterestRate = req.Rate,
            Notes = req.Notes ?? "",
            CreatedByUserId = actor.Id,
        };

        if (req.GroupId is int gid)
        {
            // Sub-lend: must target an existing, still-active collateral.
            var mates = await entries.ListForGroupAsync(gid, tracked: true);
            if (mates.Count == 0) throw ApiException.NotFound("Collateral group not found");
            if (mates.All(x => x.Status != "Active"))
                throw ApiException.Conflict("This collateral is already released");
            entry.GroupId = mates[0].GroupId;
            await entries.AddAsync(entry);
            await entries.SaveAsync(); // assigns entry.Id — the ledger needs it
        }
        else
        {
            // Fresh collateral: the entry becomes its own group (GroupId = Id).
            (entry.Id, _) = await entries.AddAsOwnGroupAsync(entry);
        }

        // The cash ledger (§3.5) books the money GOING OUT: lending
        // −AmountLend on the lend day. The release legs (principal +
        // interest) and partial returns book it coming BACK.
        await cash.AddAsync(new CashLedger
        {
            LedgerDate = entry.EntryDate,
            Amount = -entry.AmountLend,
            Source = "Lend",
            GroupId = entry.GroupId,
            ReferenceId = entry.Id,
        });
        await cash.SaveAsync();

        // The audit row: who added this lend (riders on the ledger save).
        await activity.AddAsync(actor, "Created", "Entry", entry.Id, entry.GroupId,
            $"{(req.GroupId is int ? "Added sub-lend" : "Added lend")} {ActivityStages.Money(entry.AmountLend)} — " +
            $"{entry.ItemDescription} for {owner?.Name ?? $"customer #{entry.CustomerId}"}");
        await activity.SaveAsync();
        return (entry.Id, entry.GroupId);
    }

    public async Task<(int Id, int GroupId)> UpdateAsync(int id, UpdateEntryRequest req, Actor actor)
    {
        var entry = await entries.FindByIdAsync(id, tracked: true)
            ?? throw ApiException.NotFound("Entry not found");
        if (entry.Status != "Active") throw ApiException.Conflict("Only active entries can be edited");
        if (req.Amount is decimal a && a <= 0) throw ApiException.BadRequest("Amount must be greater than 0");

        // The audit diff — capture the old values before anything mutates.
        var oldCustomerId = entry.CustomerId;
        var oldCustomerName = (await customers.FindByIdAsync(entry.CustomerId))?.Name;
        var oldItem = entry.ItemDescription;
        var oldDate = entry.EntryDate;
        var oldAmount = entry.AmountLend;
        var oldRate = entry.InterestRate;
        var oldNotes = entry.Notes;

        string? newCustomerName = null;
        if (req.CustomerId is int cid && cid != entry.CustomerId)
        {
            if (!await entries.CustomerExistsAsync(cid))
                throw ApiException.BadRequest("Customer not found");
            newCustomerName = (await customers.FindByIdAsync(cid))?.Name;
            entry.CustomerId = cid;
        }
        if (req.Item is not null && req.Item.Trim().Length > 0) entry.ItemDescription = req.Item.Trim();
        if (req.Date is DateOnly d) entry.EntryDate = d;
        if (req.Amount is decimal amt) entry.AmountLend = amt;
        // Rate: a number sets it; "" clears it (interest-free); absent leaves it alone.
        if (req.Rate is string rawRate)
        {
            var trimmed = rawRate.Trim();
            if (trimmed.Length == 0) entry.InterestRate = null;
            else if (!decimal.TryParse(trimmed, out var parsed))
                throw ApiException.BadRequest("Interest rate must be a number");
            else entry.InterestRate = parsed <= 0 ? null : parsed;
        }
        if (req.Notes is not null) entry.Notes = req.Notes;
        entry.ModifiedAt = DateTime.UtcNow;
        await entries.SaveAsync();

        // Human-readable diff of what actually changed (field: old → new).
        var changes = new List<string>();
        if (newCustomerName is not null)
            changes.Add($"customer {oldCustomerName ?? $"#{oldCustomerId}"} → {newCustomerName}");
        if (entry.ItemDescription != oldItem)
            changes.Add($"item {oldItem} → {entry.ItemDescription}");
        if (entry.EntryDate != oldDate)
            changes.Add($"date {oldDate:dd/MM/yyyy} → {entry.EntryDate:dd/MM/yyyy}");
        if (entry.AmountLend != oldAmount)
            changes.Add($"amount {ActivityStages.Money(oldAmount)} → {ActivityStages.Money(entry.AmountLend)}");
        if (entry.InterestRate != oldRate)
            changes.Add($"rate {RateText(oldRate)} → {RateText(entry.InterestRate)}");
        if (entry.Notes != oldNotes)
            changes.Add("notes updated");

        // A lend edit keeps its 'Lend' ledger row in step (date + amount).
        var ledgerRow = await cash.FindForSourceAsync("Lend", id, tracked: true);
        if (ledgerRow is not null)
        {
            ledgerRow.LedgerDate = entry.EntryDate;
            ledgerRow.Amount = -entry.AmountLend;
            await cash.SaveAsync();
        }

        // The audit row: who edited this lend, and what moved.
        await activity.AddAsync(actor, "Updated", "Entry", entry.Id, entry.GroupId,
            $"Edited lend — {string.Join("; ", changes)}");
        await activity.SaveAsync();
        return (entry.Id, entry.GroupId);
    }

    private static string RateText(decimal? r) => r is null ? "interest-free" : $"{r}%";

    internal async Task<List<EntryItem>> MapWithFifoPaid(List<Entry> list)
    {
        // FIFO allocation of each group's partial returns (±§4.6) — the
        // basis for both `paid` and live interest accrual.
        var allPayments = await payments.ListAsync();
        var paidMap = new Dictionary<int, decimal>();
        foreach (var g in list.GroupBy(e => e.GroupId))
            foreach (var (eid, covered) in Interest.AllocatePaid(g.ToList(),
                         allPayments.Where(p => p.GroupId == g.Key).Sum(p => p.Amount)))
                paidMap[eid] = covered;

        var today = DateOnly.FromDateTime(DateTime.Today);
        return list.Select(e => EntryMapper.ToItem(e, paidMap.GetValueOrDefault(e.Id), today)).ToList();
    }
}