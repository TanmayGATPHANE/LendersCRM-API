using AMS.Api.Repositories;

namespace AMS.Api.Services;

public interface IInsightsService
{
    Task<InsightsResult> GetAsync(string? tab);
}

/** The headline numbers above the dashboard — backend-computed on EVERY
    call, so any add/edit/payment/release shows fresh values as soon as
    the page refetches (plan §3.1, §6). `tab` slices the books by status:

      tab = all/null  (default) — the global books:
        totalAmanat     — Σ active lends − the partials received against
                          those still-active collaterals.
        interestEarned  — Σ InterestCollected from releases dated within
                          the Indian FY (1 Apr – 31 Mar).
        cashAvailable   — SUM(CashLedger.Amount): the stored cash table
                          (§3.5) — lends out, partial returns, the two
                          entries each release posts, and manual ±.

      tab = active    — the working set:
        totalAmanat     — same money-in-customers'-hands figure.
        interestEarned  — interest ACCRUED LIVE on active lends (basis:
                          amount − FIFO-allocated partials), which freezes
                          at release.
        cashAvailable   — cash the drawer GAVE OUT on active collaterals
                          (lends − partials received) = −Total Amanat:
                          every rupee lent out is cash not in hand.

      tab = released  — the closed collaterals:
        totalAmanat     — principal recovered (amountReturned − interest).
        interestEarned  — Σ interest frozen on the released lends.
        cashAvailable   — the two together: what release days brought in. */
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

        var activeEntries = allEntries.Where(e => e.Status == "Active").ToList();
        var activeGroupIds = activeEntries.Select(e => e.GroupId).ToHashSet();
        if (slice == "active")
        {
            var receivedOnActive = allPayments
                .Where(p => activeGroupIds.Contains(p.GroupId)).Sum(p => p.Amount);
            // Accrued live per lend on its FIFO share of the group's
            // partials — the exact figure the release will freeze.
            var groupPaid = activeEntries.GroupBy(e => e.GroupId)
                .ToDictionary(
                    g => g.Key,
                    g => Interest.AllocatePaid(g, allPayments
                        .Where(p => p.GroupId == g.Key).Sum(p => p.Amount)));
            var interestAccrued = activeEntries.Sum(e =>
                Interest.Accrued(
                    e.AmountLend - Paid(groupPaid, e),
                    e.InterestRate, e.EntryDate, DateOnly.FromDateTime(DateTime.Today)));

            return new InsightsResult(
                TotalAmanat: Math.Max(0m, activeEntries.Sum(e => e.AmountLend) - receivedOnActive),
                InterestEarned: interestAccrued,
                CashAvailable: receivedOnActive - activeEntries.Sum(e => e.AmountLend));
        }

        if (slice == "released")
        {
            var released = allEntries.Where(e => e.Status == "Released").ToList();
            var recovered = released
                .Sum(e => (e.AmountReturned ?? 0m) - (e.InterestCollected ?? 0m));
            var collected = released.Sum(e => e.InterestCollected ?? 0m);

            return new InsightsResult(
                TotalAmanat: Math.Max(0m, recovered),
                InterestEarned: collected,
                CashAvailable: Math.Max(0m, recovered) + Math.Max(0m, collected));
        }

        // "all" (and anything unrecognised) — the global books.
        // Cash now reads straight from the stored CashLedger table (§3.5):
        // partial returns, the release-principal/interest rows and the
        // manual ± all sit there; SUM(Amount) IS Total Cash Available.
        var ledger = await cash.ListAsync();
        var activePrincipal = allEntries
            .Where(e => e.Status == "Active").Sum(e => e.AmountLend);
        var interestEarned = allEntries
            .Where(e => e.Status == "Released"
                     && e.ReleasedDate is { } d && d >= fyStart && d <= fyEnd)
            .Sum(e => e.InterestCollected ?? 0m);

        return new InsightsResult(
            TotalAmanat: Math.Max(0m, activePrincipal - allPayments
                .Where(p => activeGroupIds.Contains(p.GroupId)).Sum(p => p.Amount)),
            InterestEarned: interestEarned,
            CashAvailable: ledger.Sum(x => x.Amount));
    }

    /// FIFO share of one entry's group partials, from the per-group maps.
    private static decimal Paid(Dictionary<int, Dictionary<int, decimal>> groupPaid, Data.Entities.Entry e) =>
        groupPaid.TryGetValue(e.GroupId, out var map) && map.TryGetValue(e.Id, out var v) ? v : 0m;
}