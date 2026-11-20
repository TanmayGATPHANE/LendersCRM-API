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
    IInsightsRepository insights, ICashLedgerRepository cash) : IInsightsService
{
    public async Task<InsightsResult> GetAsync(string? tab, DateOnly? from, DateOnly? to)
    {
        // Amanat + Interest are computed with SQL aggregates (the still-
        // active lends are the only rows materialized, for their FIFO
        // accrual). Total Cash Available is the whole cash table's SUM —
        // never sliced by the tab or the month/year window: what sits in
        // the drawer does not shrink because the table shows one month.
        var slices = await insights.GetAsync(
            tab, from, to, DateOnly.FromDateTime(DateTime.Today));
        var cashAvailable = await cash.TotalAsync();

        return new InsightsResult(
            TotalAmanat: slices.TotalAmanat,
            InterestEarned: slices.Interest,
            CashAvailable: cashAvailable);
    }
}
