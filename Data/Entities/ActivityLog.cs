namespace AMS.Data.Entities;

/// One row of the audit trail (§10): every create/edit/release/delete any
/// signed-in user performs lands here — who did it (the JWT's user id plus
/// a NAME SNAPSHOT, so the trail still reads right if an account is later
/// renamed), what they did (action), what it touched (entity + id), and a
/// human-readable sentence for the Activity page.
public class ActivityLog
{
    public int Id { get; set; }
    /// When the change happened (UTC; the UI shows local time).
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    /// The acting user (null only if a request ever arrives unauthenticated).
    public int? UserId { get; set; }
    /// The actor's display name, snapshotted at write time.
    public string UserName { get; set; } = "";
    /// Created | Updated | Released | Deleted — the coarse verb.
    public string Action { get; set; } = "";
    /// Entry | Customer | Payment | CashLedger | User | Group.
    public string Entity { get; set; } = "";
    /// The id of the row the action touched.
    public int EntityId { get; set; }
    /// For anything on a collateral group — entries, partial returns,
    /// releases — so a group's whole history can be fetched with one WHERE.
    public int? GroupId { get; set; }
    /// The human-readable record, e.g. "Edited lend: amount 20,000 → 25,000".
    public string Details { get; set; } = "";
}