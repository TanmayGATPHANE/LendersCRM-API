namespace AMS.Api;

/**
 * Request/response shapes for every endpoint (notes/requirements-plan.md §6).
 * The JSON responses are camelCase and shaped like the data the frontend's
 * mock API already returned, so swapping the mock for these endpoints is a
 * client-file change only — no React page moves. Validation lives in the
 * service layer so every error reaches the UI as `{ message }`.
 */

// ---- auth ----
public record LoginRequest(string Email, string Password);

// ---- users (Admin only) ----
public record NewUserRequest(string Name, string Email, string Password, string Role);
public record UpdateUserRequest(string? Name, string? Role, bool? IsActive, string? Password);

// ---- customers ----
public record NewCustomerRequest(string Name, string Phone, string Address);
public record UpdateCustomerRequest(string Name, string Phone, string Address);
public record FindOrCreateCustomerRequest(string Name, string Address);

// ---- entries ----
public record NewEntryRequest(
    int CustomerId, DateOnly Date, string Item,
    decimal Amount, decimal? Rate, string Notes, int? GroupId);
public record UpdateEntryRequest(
    int? CustomerId, string? Item, DateOnly? Date, decimal? Amount, string? Rate, string? Notes);
// UpdateEntryRequest.Rate rides as a string so three states survive JSON:
// a number sets the rate; an empty string clears it (interest-free);
// a missing field leaves it untouched.

// ---- partial returns ----
public record NewPaymentRequest(DateOnly Date, decimal Amount, string? Notes);
public record UpdatePaymentRequest(DateOnly? Date, decimal? Amount, string? Notes);

// ---- group operations ----
public record MoveGroupRequest(int CustomerId);
public record ReleaseRequest(DateOnly DateReturned, decimal? InterestCollected, decimal? TotalCollected, string? Notes);

// ---- global search (the header box) ----
/// One hit of the header's global search. Type: 'customer' | 'user' |
/// 'lend' | 'payment'; Amount rides on transaction hits; GroupId is the
/// jump-target (the collateral group) on lending rows.
public sealed record SearchHit(
    int Id, string Type, string Title, string Subtitle, decimal? Amount, int? GroupId);

/// Three sections for the dropdown — Users is filled only for Admin
/// callers (Lender/ViewOnly never see accounts).
public sealed record SearchResults(
    List<SearchHit> Customers, List<SearchHit> Users, List<SearchHit> Transactions);

// ---- manual cash movements (+ into the drawer / − out) ----
public record NewManualCashRequest(DateOnly Date, decimal Amount, string? Notes);

// ---- activity (audit trail, Admin only) ----
/// One row of the audit trail: who did what, when, to which entity. The
/// Activity tab (Admin) lists them; a collateral group's rows power the
/// per-transaction history panel.
public sealed record ActivityItem(
    int Id, DateTime AtUtc, int? UserId, string UserName,
    string Action, string Entity, int EntityId, int? GroupId, string Details);

// ---- responses ----
public sealed record UserItem(int Id, string Name, string Email, string Role, bool IsActive);
public sealed record CustomerItem(int Id, string Name, string Phone, string Address, int ActiveCount, decimal Outstanding);
public sealed record PaymentItem(int Id, int GroupId, DateOnly Date, decimal Amount, string? Notes);
/// One row of the CashLedger — the Total Cash Available table (§3.5).
/// Source: 'Lend' | 'PartialReturn' | 'ReleasePrincipal' | 'ReleaseInterest'
/// | 'Manual'. Lend rows book money goingOUT (−AmountLend, ReferenceId =
/// the entry id); PartialReturn rows book a payment (ReferenceId = its
/// PartialReturns.Id); each release posts TWO rows dated the release day;
/// Manual rows are signed ± and are the only deletable kind.
public sealed record CashLedgerItem(int Id, DateOnly Date, decimal Amount, string Source, int? GroupId, int? ReferenceId, string? Notes);

/// One entry as the dashboard's table/grid expects it (mirrors the mock).
public sealed record EntryItem
{
    public int Id { get; init; }
    public int GroupId { get; init; }
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
    public DateOnly Date { get; init; }
    /// The lending year, stored on the entry (business requirement) — always
    /// the year of Date, stamped by the backend.
    public int Year { get; init; }
    public string Item { get; init; } = "";
    public decimal Amount { get; init; }
    /// % per month; null = interest-free.
    public decimal? Rate { get; init; }
    public string Status { get; init; } = "";
    /// Accrued live on outstanding principal for active lends; the frozen
    /// InterestCollected once released (§4.4).
    public decimal Interest { get; init; }
    /// Principal of this lend already covered by partial returns (FIFO).
    public decimal Paid { get; init; }
    public string Notes { get; init; } = "";
    public DateOnly? ReleasedDate { get; init; }
    public decimal? InterestCollected { get; init; }
    public decimal? AmountReturned { get; init; }
    public decimal? TotalCollected { get; init; }
}

public sealed record CustomerDetail(int Id, string Name, string Phone, string Address);
public sealed record GroupItem(
    int GroupId, string Item, string Status,
    CustomerDetail Customer, List<EntryItem> Entries, List<PaymentItem> Payments);
public sealed record ReleaseResult(int GroupId, int PrimaryEntryId, decimal? TotalCollected, int ReleasedCount);
public sealed record InsightsResult(decimal TotalAmanat, decimal InterestEarned, decimal CashAvailable);

// ---- Reports page (§3.6) — every row is backend-computed; the pages only display.

/// One money row per month of the cash ledger: what went out, what came
/// back, and the net the drawer gained or gave up in that month.
public sealed record SummaryReportRow(
    string Month, // "2026-10"
    decimal LentOut, // Σ |Lend| rows (money out)
    decimal PrincipalIn, // partial returns + release principal
    decimal InterestIn, // release interest legs
    decimal ManualIn, // manual + rows
    decimal ManualOut, // manual − rows (positive)
    decimal Net); // the ledger's SUM for the month (negative = drawer down)

/// Per-customer lifetime track record across all their collaterals.
public sealed record CustomerReportRow(
    int CustomerId, string Name, string Address,
    int Lends, int ActiveLends,
    decimal TotalLent, decimal ReturnedPrincipal, decimal InterestCollected,
    decimal ActiveOutstanding, decimal InterestAccruing);

/// Per-released-collateral outcome: how long the money was out, what came
/// back, and the counter's actual cash figure (§4.2).
public sealed record ReleaseReportRow(
    int GroupId, string Customer, string Item,
    decimal Lent, string? ReleasedDate, int DaysHeld,
    decimal RecoveredPrincipal, decimal InterestCollected, decimal? TotalCollected);