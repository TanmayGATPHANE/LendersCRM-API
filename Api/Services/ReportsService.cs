using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IReportsService
{
    /// One money row per month (from the stored cash ledger, §3.5): what
    /// went out, what came back, and the month's net. `from`/`to` (both
    /// inclusive) scope the window; null = all time.
    Task<List<SummaryReportRow>> SummaryAsync(DateOnly? from, DateOnly? to);

    /// Per-customer track record (§3.2's History, widened) across the
    /// lends made inside the window: what was lent, what came back and
    /// what is still accruing. Nulls = all time.
    Task<List<CustomerReportRow>> CustomersAsync(DateOnly? from, DateOnly? to);

    /// Per-released-collateral outcome (§4.2): days held, principal
    /// recovered, interest frozen, the counter's actual cash — releases
    /// dated inside the window. Nulls = all time.
    Task<List<ReleaseReportRow>> ReleasesAsync(DateOnly? from, DateOnly? to);
}

/** The Reports page's numbers (plan §3.6) — computed here, in the API,
    the same way every other money figure is (the pages only display them).
    Reads flow through the existing repositories, never the DbContext. */
public sealed class ReportsService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash) : IReportsService
{
    public async Task<List<SummaryReportRow>> SummaryAsync(DateOnly? from, DateOnly? to)
    {
        var rows = await cash.ListAsync();
        if (from is { } f) rows = rows.Where(x => x.LedgerDate >= f).ToList();
        if (to is { } t) rows = rows.Where(x => x.LedgerDate <= t).ToList();

        return rows
            .GroupBy(x => (x.LedgerDate.Year, x.LedgerDate.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new SummaryReportRow(
                Month: $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                // Lend rows book money going OUT — shown as a positive "out" column.
                LentOut: g.Where(x => x.Source == "Lend").Sum(x => -x.Amount),
                PrincipalIn: g.Where(x => x.Source is "PartialReturn" or "ReleasePrincipal").Sum(x => x.Amount),
                InterestIn: g.Where(x => x.Source == "ReleaseInterest").Sum(x => x.Amount),
                ManualIn: g.Where(x => x.Source == "Manual" && x.Amount > 0).Sum(x => x.Amount),
                ManualOut: g.Where(x => x.Source == "Manual" && x.Amount < 0).Sum(x => -x.Amount),
                Net: g.Sum(x => x.Amount)))
            .ToList();
    }

    public async Task<List<CustomerReportRow>> CustomersAsync(DateOnly? from, DateOnly? to)
    {
        var all = await entries.ListAsync(status: null, search: null);
        var allPayments = await payments.ListAsync();
        if (from is { } f)
        {
            all = all.Where(e => e.EntryDate >= f).ToList();
            allPayments = allPayments.Where(p => p.PaymentDate >= f).ToList();
        }
        if (to is { } t)
        {
            all = all.Where(e => e.EntryDate <= t).ToList();
            allPayments = allPayments.Where(p => p.PaymentDate <= t).ToList();
        }
        var today = DateOnly.FromDateTime(DateTime.Today);

        var rows = new List<CustomerReportRow>();
        foreach (var byCustomer in all.GroupBy(e => e.CustomerId))
        {
            var mine = byCustomer.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToList();
            var active = mine.Where(e => e.Status == "Active").ToList();
            var released = mine.Where(e => e.Status == "Released").ToList();
            var groupIds = mine.Select(e => e.GroupId).ToHashSet();
            var activeGroupIds = active.Select(e => e.GroupId).ToHashSet();

            // Partials against still-active groups sit in the outstanding;
            // partials against released groups are already folded into the
            // release legs (AmountReturned), but they too came back early —
            // both count as money returned to the counter.
            var partialsOnActive = allPayments
                .Where(p => activeGroupIds.Contains(p.GroupId)).Sum(p => p.Amount);
            var partialsAll = allPayments
                .Where(p => groupIds.Contains(p.GroupId)).Sum(p => p.Amount);

            // The live accrual mirrors the Active tab's tiles: each lend on
            // its FIFO share of the group's partials (§4.6).
            var groupPaid = active.GroupBy(e => e.GroupId).ToDictionary(
                g => g.Key,
                g => Interest.AllocatePaid(g, allPayments
                    .Where(p => p.GroupId == g.Key).Sum(p => p.Amount)));
            decimal PaidOn(Entry e) =>
                groupPaid.TryGetValue(e.GroupId, out var map) && map.TryGetValue(e.Id, out var v) ? v : 0m;

            var first = mine[0];
            rows.Add(new CustomerReportRow(
                CustomerId: byCustomer.Key,
                Name: first.Customer?.Name ?? "?",
                Address: first.Customer?.Address ?? "",
                Lends: mine.Count,
                ActiveLends: active.Count,
                TotalLent: mine.Sum(e => e.AmountLend),
                ReturnedPrincipal: partialsAll
                    + released.Sum(e => (e.AmountReturned ?? 0m) - (e.InterestCollected ?? 0m)),
                InterestCollected: released.Sum(e => e.InterestCollected ?? 0m),
                ActiveOutstanding: active.Sum(e => e.AmountLend) - partialsOnActive,
                InterestAccruing: active.Sum(e =>
                    Interest.Accrued(e.AmountLend - PaidOn(e), e.InterestRate, e.EntryDate, today))));
        }
        return rows.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task<List<ReleaseReportRow>> ReleasesAsync(DateOnly? from, DateOnly? to)
    {
        var all = await entries.ListAsync(status: null, search: null);
        var released = all.Where(e => e.Status == "Released");
        if (from is { } f) released = released.Where(e => e.ReleasedDate >= f);
        if (to is { } t) released = released.Where(e => e.ReleasedDate <= t);
        return released
            .GroupBy(e => e.GroupId)
            .Select(g =>
            {
                var subs = g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToList();
                var primary = subs[0]; // the earliest lend headlines the group
                var recovered = subs.Sum(e => (e.AmountReturned ?? 0m) - (e.InterestCollected ?? 0m));
                var days = primary.ReleasedDate is { } d
                    ? Math.Max(0, d.DayNumber - primary.EntryDate.DayNumber)
                    : 0;
                return new ReleaseReportRow(
                    GroupId: g.Key,
                    Customer: primary.Customer?.Name ?? "?",
                    Item: primary.ItemDescription,
                    Lent: subs.Sum(e => e.AmountLend),
                    ReleasedDate: primary.ReleasedDate?.ToString("yyyy-MM-dd"),
                    DaysHeld: days,
                    RecoveredPrincipal: recovered,
                    InterestCollected: subs.Sum(e => e.InterestCollected ?? 0m),
                    TotalCollected: primary.TotalCollected);
            })
            .OrderByDescending(r => r.ReleasedDate).ThenBy(r => r.GroupId)
            .ToList();
    }
}