namespace AMS.Data.Entities;

/// One lending transaction (plan §5). The first lend of a collateral makes
/// the group (GroupId = its own id); sub-lends share the group's id.
public class Entry
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly EntryDate { get; set; }
    /// The lending YEAR, stored (business requirement): filters/reports key
    /// off the year directly instead of re-deriving it from the date.
    public int EntryYear { get; set; }
    /// What the customer handed over, e.g. "Gold chain (12g)".
    public string ItemDescription { get; set; } = "";
    public decimal AmountLend { get; set; }
    /// % per month; null = interest-free (§4.4).
    public decimal? InterestRate { get; set; }
    public DateOnly? ExpectedReturn { get; set; }
    /// Active | Released.
    public string Status { get; set; } = "Active";
    /// Principal still out on this lend (+ its frozen interest) at release.
    public decimal? AmountReturned { get; set; }
    /// Interest frozen at the release date (§4.2).
    public decimal? InterestCollected { get; set; }
    /// TotalCollected — the ACTUAL cash received at release, on the group's
    /// primary (earliest) entry for the books (§4.2).
    public decimal? TotalCollected { get; set; }
    public DateOnly? ReleasedDate { get; set; }
    public string Notes { get; set; } = "";
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
}