using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface ICashLedgerService
{
    Task<List<CashLedgerItem>> ListAsync(DateOnly? from = null, DateOnly? to = null);
    Task<decimal> TotalAsync();
    Task<CashLedgerItem> AddManualAsync(NewManualCashRequest req, Actor actor);
    Task DeleteAsync(int id, Actor actor);
}

/// The Total Cash Available table's own service (plan §3.5). The LENDING
/// rows are written by PaymentService / GroupService alongside the books
/// (same transaction); this service handles the manual ± rows
/// (outside the business: + in, − out) and reads the table for the page.
public sealed class CashLedgerService(
    ICashLedgerRepository ledger, IActivityRepository activity,
    IEntryRepository entries) : ICashLedgerService
{
    public async Task<List<CashLedgerItem>> ListAsync(DateOnly? from, DateOnly? to)
    {
        var list = await ledger.ListAsync(from, to);

        // Label the window's rows with their collateral's customer + item
        // (the group's earliest lend) — only the groups in this slice are
        // read, never the whole entries table.
        var groupIds = list.Where(x => x.GroupId is not null)
            .Select(x => x.GroupId!.Value).Distinct().ToList();
        var labels = new Dictionary<int, (string Customer, string Item)>();
        if (groupIds.Count > 0)
        {
            var groupEntries = await entries.ListForGroupsAsync(groupIds);
            foreach (var group in groupEntries.GroupBy(e => e.GroupId))
            {
                var primary = group.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).First();
                labels[group.Key] = (primary.Customer?.Name ?? "", primary.ItemDescription);
            }
        }

        return list
            .Select(x => ToItem(x, x.GroupId is int gid
                ? labels.GetValueOrDefault(gid)
                : default))
            .ToList();
    }

    public Task<decimal> TotalAsync() => ledger.TotalAsync();

    public async Task<CashLedgerItem> AddManualAsync(NewManualCashRequest req, Actor actor)
    {
        // Zero is meaningless — the sign decides the direction, + in or − out.
        if (req.Amount == 0)
            throw ApiException.BadRequest("Enter the amount (with − in front for money taken out)");
        if (req.Amount < -100_000_000m || req.Amount > 100_000_000m)
            throw ApiException.BadRequest("Amount looks unrealistic");

        var row = new CashLedger
        {
            LedgerDate = req.Date,
            Amount = req.Amount,
            Source = "Manual",
            Notes = req.Notes,
            RecordedByUserId = actor.Id,
        };
        await ledger.AddAsync(row);
        await ledger.SaveAsync();

        // The audit row: who moved money over the counter, outside the books.
        await activity.AddAsync(actor, "Created", "CashLedger", row.Id, groupId: null,
            $"Recorded manual cash {(req.Amount > 0 ? "+" : "−")}{ActivityStages.Money(Math.Abs(req.Amount))}" +
            (string.IsNullOrWhiteSpace(req.Notes) ? "" : $" ({req.Notes})"));
        await activity.SaveAsync();
        return ToItem(row);
    }

    public async Task DeleteAsync(int id, Actor actor)
    {
        var row = await ledger.FindByIdAsync(id, tracked: true)
            ?? throw ApiException.NotFound("Cash entry not found");
        // Lending rows have books behind them — a wrong partial return is
        // corrected on its payment record, never deleted here.
        if (row.Source != "Manual")
            throw ApiException.Conflict("Only manual (±) cash entries can be deleted — lending rows belong to their lend/release records");

        var details = $"Deleted manual cash movement {ActivityStages.Money(row.Amount)}" +
                      (string.IsNullOrWhiteSpace(row.Notes) ? "" : $" ({row.Notes})");
        ledger.Remove(row);
        await ledger.SaveAsync();

        // The audit row: deletions must keep their trail too.
        await activity.AddAsync(actor, "Deleted", "CashLedger", id, groupId: null, details);
        await activity.SaveAsync();
    }

    private static CashLedgerItem ToItem(CashLedger x, (string Customer, string Item) label = default) =>
        new(x.Id, x.LedgerDate, x.Amount, x.Source, x.GroupId, x.ReferenceId, x.Notes,
            x.GroupId is null ? null : label.Customer,
            x.GroupId is null ? null : label.Item);
}