using System.Globalization;
using AMS.Api.Services; // Interest — the shared, pure domain math
using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IGroupQueryRepository
{
    /// The dashboard's page of group ids + how many groups the filters
    /// match. Filtering, sorting and skip/take all run in SQL — the page
    /// is the only part of the book the service materializes.
    Task<(List<int> Ids, int Total)> PageGroupIdsAsync(GroupListQuery query);
    /// Every lending year (entries + partial returns) — the year dropdown.
    Task<List<string>> ListYearsAsync();
}

/// The grouped, paged read side of the dashboard (plan §3.1). A "group" is
/// one collateral: every entry sharing GroupId. Doing the tab / month-year
/// / search filters and the sort BEFORE the page keeps a 50-year book from
/// ever crossing the wire — the old client fetched every entry and every
/// partial return on load and filtered in memory.
public sealed class GroupQueryRepository(AmsDbContext db) : IGroupQueryRepository
{
    /// The views' base query: every lend EXCEPT outsourced ones (§4.8),
    /// matching EntryRepository.ListableView.
    private IQueryable<Entry> ViewEntries() =>
        db.Entries.AsNoTracking().Where(e => !e.IsOutsourced);

    public async Task<(List<int> Ids, int Total)> PageGroupIdsAsync(GroupListQuery query)
    {
        var take = Math.Clamp(query.Take, 1, 200);
        var skip = Math.Max(0, query.Skip);

        var stats = BuildStats(query);
        var total = await stats.CountAsync();

        var col = (query.Sort ?? "date").Trim().ToLowerInvariant();
        var dir = string.Equals(query.Dir, "desc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

        // The Interest column is not SQL-expressible (FIFO partials + the
        // day-based accrual), so that ONE sort materializes the matching
        // groups' lends and ranks them in memory; every other sort stays
        // in SQL. The transfer is bounded to the matching window.
        if (col == "interest")
        {
            var orderedIds = await InterestOrderedIdsAsync(stats, dir);
            return (orderedIds.Skip(skip).Take(take).ToList(), total);
        }

        var ids = await ApplySort(stats, col, dir)
            .Skip(skip).Take(take)
            .Select(s => s.GroupId)
            .ToListAsync();
        return (ids, total);
    }

    public async Task<List<string>> ListYearsAsync()
    {
        var entryYears = await ViewEntries()
            .Select(e => e.EntryDate.Year).Distinct().ToListAsync();
        var paymentYears = await db.PartialReturns.AsNoTracking()
            .Select(p => p.PaymentDate.Year).Distinct().ToListAsync();
        return entryYears.Concat(paymentYears).Distinct()
            .OrderByDescending(y => y)
            .Select(y => y.ToString(CultureInfo.InvariantCulture))
            .ToList();
    }

    /// One row per collateral group, carrying only what the filters and
    /// sort read — the heavy row content is built for the page only.
    private sealed class GroupStat
    {
        public int GroupId { get; init; }
        public DateOnly FirstDate { get; init; }
        public bool HasActive { get; init; }
        public string CustomerName { get; init; } = "";
        public decimal PrimaryAmount { get; init; }
        public decimal? PrimaryRate { get; init; }
        public decimal? PrimaryTotalCollected { get; init; }
        public decimal Earned { get; init; }
    }

    private IQueryable<GroupStat> BuildStats(GroupListQuery query)
    {
        // A collateral belongs to the month/year window through its FIRST
        // lend (the table row's date) — same rule InsightsService uses, so
        // tiles and rows always agree.
        var stats = ViewEntries()
            .GroupBy(e => e.GroupId)
            .Select(g => new GroupStat
            {
                GroupId = g.Key,
                FirstDate = g.Min(e => e.EntryDate),
                HasActive = g.Any(e => e.Status == "Active"),
                CustomerName = g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
                    .Select(e => e.Customer!.Name).First(),
                PrimaryAmount = g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
                    .Select(e => e.AmountLend).First(),
                PrimaryRate = g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
                    .Select(e => e.InterestRate).First(),
                PrimaryTotalCollected = g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
                    .Select(e => e.TotalCollected).First(),
                Earned = g.Sum(e => e.InterestCollected ?? 0m),
            });

        var tab = (query.Tab ?? "").Trim().ToLowerInvariant();
        if (tab == "active") stats = stats.Where(s => s.HasActive);
        else if (tab == "released") stats = stats.Where(s => !s.HasActive);

        if (MonthOf(query.Month) is int month)
            stats = stats.Where(s => s.FirstDate.Month == month);
        if (int.TryParse(query.Year, out var year) && year > 0)
            stats = stats.Where(s => s.FirstDate.Year == year);

        // Multi-word search is ALL-of, like the UI's matcher: each word
        // must hit the group somewhere (its lends or its partial returns).
        foreach (var word in Words(query.Search))
            stats = stats.Where(s => WordGroupIds(word).Contains(s.GroupId));

        return stats;
    }

    /// The group ids one search word reaches: customer name, item, notes,
    /// dates (ISO, year, month name/number, day) and exact amounts on the
    /// lends and the partial returns. Evaluated in SQL.
    private IQueryable<int> WordGroupIds(string word)
    {
        var term = word.Trim().ToLowerInvariant();

        var ids = ViewEntries()
            .Where(e => e.ItemDescription.ToLower().Contains(term)
                     || e.Customer!.Name.ToLower().Contains(term)
                     || e.Notes.ToLower().Contains(term))
            .Select(e => e.GroupId);

        ids = ids.Concat(db.PartialReturns.AsNoTracking()
            .Where(p => p.Notes != null && p.Notes.ToLower().Contains(term))
            .Select(p => p.GroupId));

        if (decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            ids = ids.Concat(ViewEntries().Where(e => e.AmountLend == amount).Select(e => e.GroupId));
            ids = ids.Concat(db.PartialReturns.AsNoTracking()
                .Where(p => p.Amount == amount).Select(p => p.GroupId));
        }

        if (DateOnly.TryParseExact(term, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            ids = ids.Concat(ViewEntries().Where(e => e.EntryDate == date).Select(e => e.GroupId));
            ids = ids.Concat(db.PartialReturns.AsNoTracking()
                .Where(p => p.PaymentDate == date).Select(p => p.GroupId));
        }

        if (MonthOf(term) is int month)
        {
            ids = ids.Concat(ViewEntries().Where(e => e.EntryDate.Month == month).Select(e => e.GroupId));
            ids = ids.Concat(db.PartialReturns.AsNoTracking()
                .Where(p => p.PaymentDate.Month == month).Select(p => p.GroupId));
        }

        if (term.Length == 4 && int.TryParse(term, out var year) && year is > 1900 and < 2200)
        {
            ids = ids.Concat(ViewEntries().Where(e => e.EntryDate.Year == year).Select(e => e.GroupId));
            ids = ids.Concat(db.PartialReturns.AsNoTracking()
                .Where(p => p.PaymentDate.Year == year).Select(p => p.GroupId));
        }

        if (int.TryParse(term, out var day) && day is >= 1 and <= 31)
        {
            ids = ids.Concat(ViewEntries().Where(e => e.EntryDate.Day == day).Select(e => e.GroupId));
            ids = ids.Concat(db.PartialReturns.AsNoTracking()
                .Where(p => p.PaymentDate.Day == day).Select(p => p.GroupId));
        }

        return ids.Distinct();
    }

    private static IOrderedQueryable<GroupStat> ApplySort(
        IQueryable<GroupStat> q, string col, string dir)
    {
        var desc = dir == "desc";
        return col switch
        {
            "customer" => desc
                ? q.OrderByDescending(s => s.CustomerName).ThenByDescending(s => s.FirstDate)
                : q.OrderBy(s => s.CustomerName).ThenBy(s => s.FirstDate),
            "amount" => desc
                ? q.OrderByDescending(s => s.PrimaryAmount).ThenByDescending(s => s.CustomerName)
                : q.OrderBy(s => s.PrimaryAmount).ThenBy(s => s.CustomerName),
            // Null rate sorts last ascending (the UI's `rate ?? -1`).
            "rate" => desc
                ? q.OrderByDescending(s => s.PrimaryRate ?? -1m).ThenByDescending(s => s.CustomerName)
                : q.OrderBy(s => s.PrimaryRate ?? -1m).ThenBy(s => s.CustomerName),
            // Total = release-day cash; open collaterals sort flat/last.
            "total" => desc
                ? q.OrderByDescending(s => s.HasActive ? 0m : (s.PrimaryTotalCollected ?? 0m))
                    .ThenByDescending(s => s.CustomerName)
                : q.OrderBy(s => s.HasActive ? 0m : (s.PrimaryTotalCollected ?? 0m))
                    .ThenBy(s => s.CustomerName),
            "earned" => desc
                ? q.OrderByDescending(s => s.Earned).ThenByDescending(s => s.CustomerName)
                : q.OrderBy(s => s.Earned).ThenBy(s => s.CustomerName),
            "status" => desc
                ? q.OrderByDescending(s => s.HasActive ? 0 : 1).ThenByDescending(s => s.FirstDate)
                : q.OrderBy(s => s.HasActive ? 0 : 1).ThenBy(s => s.FirstDate),
            _ => desc
                ? q.OrderByDescending(s => s.FirstDate).ThenByDescending(s => s.GroupId)
                : q.OrderBy(s => s.FirstDate).ThenBy(s => s.GroupId),
        };
    }

    /// Interest-ordered group ids. The display figure is the shared domain
    /// math (frozen on released lends, live accrue-minus-FIFO-paids on
    /// active ones), so it is computed here over the matching groups'
    /// lends — only for this sort, never for the default page load.
    private async Task<List<int>> InterestOrderedIdsAsync(IQueryable<GroupStat> stats, string dir)
    {
        var rows = await stats.Select(s => new { s.GroupId, s.CustomerName }).ToListAsync();
        var names = rows.ToDictionary(r => r.GroupId, r => r.CustomerName);
        var ids = rows.Select(r => r.GroupId).ToList();
        if (ids.Count == 0) return [];

        var groupEntries = await db.Entries.AsNoTracking()
            .Where(e => ids.Contains(e.GroupId))
            .OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
            .ToListAsync();
        var paidTotals = await db.PartialReturns.AsNoTracking()
            .Where(p => ids.Contains(p.GroupId))
            .GroupBy(p => p.GroupId)
            .Select(g => new { GroupId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.GroupId, x => x.Total);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var keys = new Dictionary<int, decimal>(ids.Count);
        foreach (var group in groupEntries.GroupBy(e => e.GroupId))
        {
            var subs = group.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToList();
            var paid = Interest.AllocatePaid(subs, paidTotals.GetValueOrDefault(group.Key));
            keys[group.Key] = subs.Sum(e => e.Status == "Released"
                ? e.InterestCollected ?? 0m
                : Interest.Accrued(e.AmountLend - paid.GetValueOrDefault(e.Id),
                    e.InterestRate, e.EntryDate, today));
        }

        IEnumerable<int> ordered = ids
            .OrderBy(id => keys.GetValueOrDefault(id))
            .ThenBy(id => names.GetValueOrDefault(id) ?? "", StringComparer.OrdinalIgnoreCase);
        return dir == "desc" ? ordered.Reverse().ToList() : ordered.ToList();
    }

    private static IEnumerable<string> Words(string? search) =>
        (search ?? "").Trim().ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static readonly string[] ShortMonths =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] FullMonths =
        ["january", "february", "march", "april", "may", "june", "july",
         "august", "september", "october", "november", "december"];

    /// "all"/blank → null; "01".."12" → 1..12; "oct"/"october" → 10.
    /// Only a PREFIX of a month name counts ("octo"), never a word that
    /// merely starts with one ("Marcus" is a customer, not March).
    private static int? MonthOf(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0 || s == "all") return null;
        if (int.TryParse(s, out var n)) return n is >= 1 and <= 12 ? n : null;
        if (s.Length < 3) return null;
        for (var i = 0; i < ShortMonths.Length; i++)
            if (ShortMonths[i].StartsWith(s) || FullMonths[i] == s)
                return i + 1;
        return null;
    }
}
