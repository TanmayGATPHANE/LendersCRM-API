namespace AMS.Data.Entities;

/// An entry DELETED from the books — archived here in full (plan §4.9).
/// The original entry's Id, GroupId and every business column are PRESERVED
/// (Id is not an identity: it IS the deleted entry's id), plus when/who
/// deleted it. Nothing is recoverable into the working tables from this
/// row automatically — it is history, read for audits.
public class DeletedEntry
{
    public int Id { get; set; }              // THE original entry's id
    public int GroupId { get; set; }
    public int CustomerId { get; set; }
    public DateOnly EntryDate { get; set; }
    public int EntryYear { get; set; }
    public string ItemDescription { get; set; } = "";
    public decimal AmountLend { get; set; }
    public decimal? InterestRate { get; set; }
    public DateOnly? ExpectedReturn { get; set; }
    public string Status { get; set; } = "Active";
    public decimal? AmountReturned { get; set; }
    public decimal? InterestCollected { get; set; }
    public decimal? TotalCollected { get; set; }
    public DateOnly? ReleasedDate { get; set; }
    public string Notes { get; set; } = "";
    public bool IsOutsourced { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    // The deletion audit — when and by whose hand, straight from the UI.
    public DateTime DeletedAtUtc { get; set; } = DateTime.UtcNow;
    public int? DeletedByUserId { get; set; }
    public string? DeleteReason { get; set; }
}
