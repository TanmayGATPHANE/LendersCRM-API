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
    the LEND date — every tile computes over the SAME rows the table
    below shows:

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
      Total Cash Available— SUM(CashLedger.Amount) inside the window —
                            lends book −, partial returns and the two
                            release legs book +, manual ± books itself.
                            No window = the whole cash table. */
public sealed class InsightsService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash) : IInsightsService
{
    public async Task<InsightsResult> GetAsync(string? tab, DateOnly? from, DateOnly? to)
    {
        var slice = (tab ?? "").Trim().ToLowerInvariant();

        // The window (the dashboard's month/year filters) slices by the
        // LEND date — the same axis the table filters, so tiles and
        // table always agree on which rows belong.
        var allEntries = (await entries.ListAsync(status: null, search: null))
            .Where(e => (!from.HasValue || e.EntryDate >= from)
                     && (!to.HasValue || e.EntryDate <= to))
            .ToList();
        var allPayments = await payments.ListAsync();
        var ledger = await cash.ListAsync(from, to);

        // The tab's rows — Total Amanat is the plain Σ AmountLend over
        // them (§3.1); the interest tile is the only tab-specific slice.
        var activeEntries = allEntries.Where(e => e.Status == "Active").ToList();
        var releasedEntries = allEntries.Where(e => e.Status == "Released").ToList();
        var tabEntries = slice == "released"
            ? releasedEntries
            : slice == "active"
                ? activeEntries
                : allEntries;
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

        return new InsightsResult(
            TotalAmanat: tabEntries.Sum(e => e.AmountLend),
            InterestEarned: interestEarned,
            // The cash inside the window when one is selected; the whole
            // cash table when the dashboard shows all months + all years.
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
