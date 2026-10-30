namespace AMS.Data.Entities;

/// The people who borrow (plan §3.2). Outstanding is never stored here —
/// it is computed from active entries minus partial returns.
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}