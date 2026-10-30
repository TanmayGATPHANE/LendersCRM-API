using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface ICashLedgerService
{
    Task<List<CashLedgerItem>> ListAsync();
    Task<CashLedgerItem> AddManualAsync(NewManualCashRequest req, int? userId);
    Task DeleteAsync(int id);
}

/// The Total Cash Available table's own service (plan §3.5). The LENDING
/// rows are written by PaymentService / GroupService alongside the books
/// (same transaction); this service handles the manual ± rows
/// (outside the business: + in, − out) and reads the table for the page.
public sealed class CashLedgerService(ICashLedgerRepository ledger) : ICashLedgerService
{
    public async Task<List<CashLedgerItem>> ListAsync()
    {
        var list = await ledger.ListAsync();
        return list.Select(ToItem).ToList();
    }

    public async Task<CashLedgerItem> AddManualAsync(NewManualCashRequest req, int? userId)
    {
        // Zero is meaningless — the sign decides the direction, + in or − out.
        if (req.Amount == 0)
            throw ApiException.BadRequest("Enter the amount (with − in front for money taken out)");
        if (req.Amount < -100_000_000m || req.Amount > 100_000_000m)
            throw ApiException.BadRequest("Amount looks unrealistic");

        var row = new CashLedger
        {
            LedgerDate = req.Date,
            Amount = req.Amount,
            Source = "Manual",
            Notes = req.Notes,
            RecordedByUserId = userId,
        };
        await ledger.AddAsync(row);
        await ledger.SaveAsync();
        return ToItem(row);
    }

    public async Task DeleteAsync(int id)
    {
        var row = await ledger.FindByIdAsync(id, tracked: true)
            ?? throw ApiException.NotFound("Cash entry not found");
        // Lending rows have books behind them — a wrong partial return is
        // corrected on its payment record, never deleted here.
        if (row.Source != "Manual")
            throw ApiException.Conflict("Only manual (±) cash entries can be deleted — lending rows belong to their lend/release records");
        ledger.Remove(row);
        await ledger.SaveAsync();
    }

    private static CashLedgerItem ToItem(CashLedger x) =>
        new(x.Id, x.LedgerDate, x.Amount, x.Source, x.GroupId, x.ReferenceId, x.Notes);
}