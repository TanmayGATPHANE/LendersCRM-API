using AMS.Data.Entities;

namespace AMS.Api.Services;

/** The ONE shared interest + FIFO-repayment implementation the whole API
    uses (plan §4.4 / §4.6) — mirrors the frontend's utils/interest.js and
    utils/repayment.js so both sides agree to the rupee. Pure domain math:
    no database, no HTTP. */
public static class Interest
{
    /// `principal × (rate ÷ 100) × (days ÷ 30)`, rounded to the whole rupee
    /// — null/0 rate or an inverted date range accrues 0.
    public static decimal Accrued(decimal principal, decimal? ratePctPerMonth, DateOnly from, DateOnly to)
    {
        if (!ratePctPerMonth.HasValue || ratePctPerMonth.Value <= 0 || principal <= 0) return 0m;
        var days = to.DayNumber - from.DayNumber;
        if (days <= 0) return 0m;
        return Math.Round(principal * (ratePctPerMonth.Value / 100m) * days / 30m, MidpointRounding.AwayFromZero);
    }

    /// Spread a group's total partial returns across its active lends,
    /// oldest first (FIFO): the earliest lend is cleared before the next
    /// one is touched. Returns entryId → amount covered.
    public static Dictionary<int, decimal> AllocatePaid(IEnumerable<Entry> groupEntries, decimal partialTotal)
    {
        var paid = new Dictionary<int, decimal>();
        var remaining = Math.Max(0m, partialTotal);
        foreach (var e in groupEntries
                     .Where(x => x.Status == "Active")
                     .OrderBy(x => x.EntryDate).ThenBy(x => x.Id))
        {
            var cover = Math.Min(remaining, e.AmountLend);
            paid[e.Id] = cover;
            remaining -= cover;
        }
        return paid;
    }
}