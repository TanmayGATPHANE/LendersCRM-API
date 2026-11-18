using AMS.Api.Repositories;

namespace AMS.Api.Services;

public interface IInsightsService
{
    Task<InsightsResult> GetAsync(string? tab);
}

/** The headline numbers above the dashboard — backend-computed on EVERY
    call, so any add/edit/payment/release shows fresh values as soon as
    the page refetches (plan §3.1, §6). The tab slices the books:

      Total Amanat        — the SUM OF LEND AMOUNTS on the tab's rows:
                            all = every lend on the books, active = the
                            active collaterals' lends, released = the
                            released ones. No deductions.
      Interest (per tab)  — all: Σ InterestCollected from releases dated
                            within the Indian FY (1 Apr – 31 Mar);
                            active: interest ACCRUED LIVE on active lends
                            (basis: amount − FIFO-allocated partials);
                            released: Σ interest frozen on the released
                            lends.
      Total Cash Available— SUM(CashLedger.Amount) — the stored cash
                            table (§3.5), NOTHING else. One figure on
                            every tab: lends book −, partial returns and
                            the two release legs book +, manual ± books
                            itself. */
public sealed class InsightsService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash) : IInsightsService
{
    public async Task<InsightsResult> GetAsync(string? tab)
    {
        var slice = (tab ?? "").Trim().ToLowerInvariant();
        // Indian FY: April to March, anchored on today's date.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fyStartYear = today.Month >= 4 ? today.Year : today.Year - 1;
        var fyStart = new DateOnly(fyStartYear, 4, 1);
        var fyEnd = new DateOnly(fyStartYear + 1, 3, 31);

        var allEntries = await entries.ListAsync(status: null, search: null);
        var allPayments = await payments.ListAsync();
        var ledger = await cash.ListAsync();

        // The tab's rows — Total Amanat is the plain Σ AmountLend over
        // them (§3.1); the interest tile is the only tab-specific slice.
        var tabEntries = slice == "released"
            ? allEntries.Where(e => e.Status == "Released").ToList()
            : slice == "active"
                ? allEntries.Where(e => e.Status == "Active").ToList()
                : allEntries;
        var interestEarned = slice == "active"
            ? AccruedLive(allEntries, allPayments)
            : slice == "released"
                ? tabEntries.Sum(e => e.InterestCollected ?? 0m)
                : allEntries
                    .Where(e => e.Status == "Released"
                             && e.ReleasedDate is { } d && d >= fyStart && d <= fyEnd)
                    .Sum(e => e.InterestCollected ?? 0m);

        return new InsightsResult(
            TotalAmanat: tabEntries.Sum(e => e.AmountLend),
            InterestEarned: interestEarned,
            // The stored cash table's own SUM — nothing else, one figure
            // on every tab (the page's tabs change the other two tiles).
            CashAvailable: ledger.Sum(x => x.Amount));
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
