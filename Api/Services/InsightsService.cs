using AMS.Api.Repositories;

namespace AMS.Api.Services;

public interface IInsightsService
{
    Task<InsightsResult> GetAsync(string? tab, DateOnly? from, DateOnly? to);
}

/** The headline numbers above the dashboard — backend-computed on EVERY
    call, so any add/edit/payment/release shows fresh values as soon as
    the page refetches (plan §3.1, §6). The tab slices the books, the
    FROM/TO window (the dashboard's month/year filters) slices them by
    the LEND date — the Amanat and Interest tiles compute over the SAME
    rows the table below shows; the cash tile deliberately does not:

      Total Amanat        — the SUM OF LEND AMOUNTS on the tab's rows:
                            all = every lend on the books, active = the
                            active collaterals' lends, released = the
                            released ones. No deductions.
      Interest (per tab)  — all: the interest EARNED so far (Σ frozen
                            InterestCollected over every release, all
                            time) PLUS the interest that will come (the
                            live accrual on still-active collaterals,
                            which future releases collect);
                            active: interest ACCRUED LIVE on active lends
                            (basis: amount − FIFO-allocated partials);
                            released: Σ interest frozen on the released
                            lends.
      Total Cash Available— SUM(CashLedger.Amount) over the WHOLE table —
                            lends book −, partial returns and the two
                            release legs book +, manual ± books itself.
                            The tab and the FROM/TO window touch the other
                            two tiles only: the cash actually in hand is a
                            property of the books, not of the current view,
                            so filtering the table must never change it. */
public sealed class InsightsService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash) : IInsightsService
{
    public async Task<InsightsResult> GetAsync(string? tab, DateOnly? from, DateOnly? to)
    {
        var slice = (tab ?? "").Trim().ToLowerInvariant();

        // The window follows the GROUP — a collateral belongs to it through
        // its FIRST lend (the dashboard table row's date), and every lend
        // of it counts with it even when one is dated outside the window.
        // Windowing per ENTRY instead made multi-year collaterals leak
        // into the wrong year's tiles while the table correctly showed
        // them elsewhere.
        var allEntries = await entries.ListAsync(status: null, search: null);
        var allPayments = await payments.ListAsync();
        var inWindow = allEntries
            .GroupBy(e => e.GroupId)
            .Where(g =>
            {
                var first = g.Min(e => e.EntryDate);
                return (!from.HasValue || first >= from) && (!to.HasValue || first <= to);
            })
            .SelectMany(g => g)
            .ToList();

        // The tab's rows — Total Amanat is the plain Σ AmountLend over
        // them (§3.1); the interest tile is the only tab-specific slice.
        var activeEntries = inWindow.Where(e => e.Status == "Active").ToList();
        var releasedEntries = inWindow.Where(e => e.Status == "Released").ToList();
        var tabEntries = slice == "released"
            ? releasedEntries
            : slice == "active"
                ? activeEntries
                : inWindow;
        var interestEarned = slice == "active"
            ? AccruedLive(activeEntries, allPayments)
            : slice == "released"
                ? releasedEntries.Sum(e => e.InterestCollected ?? 0m)
                // All tab: the interest EARNED so far (every release's
                // frozen figure, all time NOT FY-bound) PLUS the interest
                // that will come — accrued today on the still-active
                // collaterals, which future releases will collect.
                : releasedEntries.Sum(e => e.InterestCollected ?? 0m)
                  + AccruedLive(activeEntries, allPayments);

        // Total Cash Available is the whole cash table's SUM — never
        // sliced by the tab or the month/year window. What sits in the
        // drawer does not shrink because the table is showing one month.
        var cashAvailable = await cash.TotalAsync();

        return new InsightsResult(
            TotalAmanat: tabEntries.Sum(e => e.AmountLend),
            InterestEarned: interestEarned,
            CashAvailable: cashAvailable);
    }

    /** Interest accruing live on active lends — FIFO share of each
        group's partials first, then the shared formula (§4.4). */
    private static decimal AccruedLive(List<Data.Entities.Entry> activeEntries, List<Data.Entities.PartialReturn> allPayments)
    {
        var groupPaid = activeEntries.GroupBy(e => e.GroupId)
            .ToDictionary(
                g => g.Key,
                g => Interest.AllocatePaid(g, allPayments
                    .Where(p => p.GroupId == g.Key).Sum(p => p.Amount)));
        return activeEntries.Sum(e =>
            Interest.Accrued(
                e.AmountLend - (groupPaid.TryGetValue(e.GroupId, out var map) &&
                                map.TryGetValue(e.Id, out var v) ? v : 0m),
                e.InterestRate, e.EntryDate, DateOnly.FromDateTime(DateTime.Today)));
    }
}
