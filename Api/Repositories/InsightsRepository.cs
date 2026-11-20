using AMS.Api.Services; // Interest — the shared, pure domain math
using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IInsightsRepository
{
    /// The two book-derived tiles (Amanat, Interest) for a tab + window,
    /// computed with SQL aggregates. The cash tile is the ledger's own
    /// SUM and comes from ICashLedgerRepository.TotalAsync.
    Task<InsightsSlices> GetAsync(string? tab, DateOnly? from, DateOnly? to, DateOnly today);
}

public sealed record InsightsSlices(decimal TotalAmanat, decimal Interest);

/// The dashboard tiles' read side (plan §3.1, §6). The old service loaded
/// every entry and every partial return to sum three numbers — here the
/// sums are SQL, and only the still-active lends (a small set by nature)
/// are materialized for their FIFO-adjusted live accrual.
public sealed class InsightsRepository(AmsDbContext db) : IInsightsRepository
{
    public async Task<InsightsSlices> GetAsync(
        string? tab, DateOnly? from, DateOnly? to, DateOnly today)
    {
        var slice = (tab ?? "").Trim().ToLowerInvariant();

        // A collateral belongs to the window through its FIRST lend (the
        // dashboard row's date) — the same rule the paged list uses.
        var windowGroups = db.Entries.AsNoTracking()
            .Where(e => !e.IsOutsourced)
            .GroupBy(e => e.GroupId)
            .Where(g => (!from.HasValue || g.Min(e => e.EntryDate) >= from.Value)
                     && (!to.HasValue || g.Min(e => e.EntryDate) <= to.Value))
            .Select(g => g.Key);

        var selected = windowGroups;
        if (slice == "active")
            selected = selected.Where(id => db.Entries.Any(
                e => e.GroupId == id && !e.IsOutsourced && e.Status == "Active"));
        else if (slice == "released")
            selected = selected.Where(id => !db.Entries.Any(
                e => e.GroupId == id && !e.IsOutsourced && e.Status == "Active"));

        var inWindow = db.Entries.AsNoTracking()
            .Where(e => !e.IsOutsourced && selected.Contains(e.GroupId));
        var tabEntries = slice switch
        {
            "active" => inWindow.Where(e => e.Status == "Active"),
            "released" => inWindow.Where(e => e.Status == "Released"),
            _ => inWindow,
        };

        var totalAmanat = await tabEntries.SumAsync(e => e.AmountLend);
        // Frozen interest over the tab's released lends (0 on the Active tab).
        var releasedInterest = await tabEntries
            .Where(e => e.Status == "Released")
            .SumAsync(e => e.InterestCollected ?? 0m);

        decimal activeInterest = 0m;
        if (slice != "released")
        {
            var active = await inWindow.Where(e => e.Status == "Active").ToListAsync();
            if (active.Count > 0)
            {
                var activeGroupIds = active.Select(e => e.GroupId).Distinct().ToList();
                var paidTotals = await db.PartialReturns.AsNoTracking()
                    .Where(p => activeGroupIds.Contains(p.GroupId))
                    .GroupBy(p => p.GroupId)
                    .Select(g => new { GroupId = g.Key, Total = g.Sum(p => p.Amount) })
                    .ToDictionaryAsync(x => x.GroupId, x => x.Total);

                foreach (var group in active.GroupBy(e => e.GroupId))
                {
                    var subs = group.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToList();
                    var paid = Interest.AllocatePaid(subs, paidTotals.GetValueOrDefault(group.Key));
                    activeInterest += subs.Sum(e => Interest.Accrued(
                        e.AmountLend - paid.GetValueOrDefault(e.Id),
                        e.InterestRate, e.EntryDate, today));
                }
            }
        }

        var interest = slice switch
        {
            "active" => activeInterest,
            "released" => releasedInterest,
            // All: earned so far (every release's frozen figure, all time)
            // plus what the still-active lends accrue.
            _ => releasedInterest + activeInterest,
        };
        return new InsightsSlices(totalAmanat, interest);
    }
}
