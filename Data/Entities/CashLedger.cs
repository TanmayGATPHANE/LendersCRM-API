namespace AMS.Data.Entities;

/// The Total Cash Available SQL table (plan §3.5). Every rupee that comes
/// back in hand is a STORED row here — nothing about cash is derived:
///   PartialReturn      — written when a partial return is recorded
///   ReleasePrincipal   — written at release ("when released, 2 entries")
///   ReleaseInterest    —        "            (the second of the two)
///   Manual             — the ± movements outside the business
/// Total Cash Available = SUM(Amount). Lending rows are written by the
/// release/payment services in the same transaction as the books change,
/// so this table can never drift from Entries/PartialReturns.
public class CashLedger
{
    public int Id { get; set; }
    public DateOnly LedgerDate { get; set; }
    /// Principal/interest/manual-in > 0; manual-out < 0.
    public decimal Amount { get; set; }
    /// 'PartialReturn' | 'ReleasePrincipal' | 'ReleaseInterest' | 'Manual'
    public string Source { get; set; } = "";
    /// The collateral group the money came back against (NULL for Manual).
    public int? GroupId { get; set; }
    /// The PartialReturn record this row books (Source = 'PartialReturn') —
    /// the link that keeps the row synced when a payment is corrected.
    public int? ReferenceId { get; set; }
    public string? Notes { get; set; }
    public int? RecordedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}