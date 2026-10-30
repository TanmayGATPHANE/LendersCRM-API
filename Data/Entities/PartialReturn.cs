namespace AMS.Data.Entities;

/// A partial return — money handed back before the final release,
/// recorded against a whole collateral group (plan §4.6). The lend
/// records themselves stay untouched (an honest history).
public class PartialReturn
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public int? RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}