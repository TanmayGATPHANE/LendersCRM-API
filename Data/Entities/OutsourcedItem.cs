namespace AMS.Data.Entities;

/// One round of an outsourced collateral (§4.8): the counter handed the
/// item to another party ("outsourced to") to sell/recover on their
/// behalf. The collateral leaves the dashboard/reports while it is out
/// (Entries.IsOutsourced = 1) and shows only on the Outsourced page;
/// marking it RETURNED stamps this row and clears the flag. An item can
/// go out several times — one row per round, the OPEN row (ReturnedDate
/// null) is the current state.
public class OutsourcedItem
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    /// The person/shop the collateral was handed to.
    public string OutsourcedTo { get; set; } = "";
    /// The day the item left the counter.
    public DateOnly OutsourceDate { get; set; }
    public string? Notes { get; set; }
    /// Set when the item comes back — the flag clears at the same moment.
    public DateOnly? ReturnedDate { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
