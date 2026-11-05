using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface ICashLedgerRepository
{
    /// The ledger rows (optionally clipped to an inclusive LedgerDate
    /// window — nulls mean unbounded), oldest first; the page re-sorts.
    Task<List<CashLedger>> ListAsync(DateOnly? from = null, DateOnly? to = null);
    /// Whole-book cash: SUM(Amount) over every row, the Total Cash
    /// Available the dashboard tile reads — window-independent.
    Task<decimal> TotalAsync();
    Task<CashLedger?> FindByIdAsync(int id, bool tracked = true);
    /// The ledger row booked from a books record (Source + ReferenceId link)
    /// — used for the edit-syncs (payments, lends).
    Task<CashLedger?> FindForSourceAsync(string source, int referenceId, bool tracked = true);
    Task AddAsync(CashLedger row);
    void Remove(CashLedger row);
    Task<int> SaveAsync();
}

/// The only layer touching the CashLedger table. Shares the scoped
/// DbContext with the other repositories, so the release/payment services
/// just AddAsync + SaveAsync and the rows land in the SAME transaction.
public sealed class CashLedgerRepository(AmsDbContext db) : ICashLedgerRepository
{
    public async Task<List<CashLedger>> ListAsync(DateOnly? from, DateOnly? to)
    {
        var q = db.CashLedger.AsNoTracking().AsQueryable();
        if (from is { } f) q = q.Where(x => x.LedgerDate >= f);
        if (to is { } t) q = q.Where(x => x.LedgerDate <= t);
        return await q.OrderBy(x => x.LedgerDate).ThenBy(x => x.Id).ToListAsync();
    }

    public Task<decimal> TotalAsync() =>
        db.CashLedger.SumAsync(x => x.Amount);

    public Task<CashLedger?> FindByIdAsync(int id, bool tracked = true) =>
        tracked
            ? db.CashLedger.FirstOrDefaultAsync(x => x.Id == id)
            : db.CashLedger.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    public Task<CashLedger?> FindForSourceAsync(string source, int referenceId, bool tracked = true) =>
        tracked
            ? db.CashLedger.FirstOrDefaultAsync(
                x => x.Source == source && x.ReferenceId == referenceId)
            : db.CashLedger.AsNoTracking().FirstOrDefaultAsync(
                x => x.Source == source && x.ReferenceId == referenceId);

    public Task AddAsync(CashLedger row)
    {
        db.CashLedger.Add(row);
        return Task.CompletedTask;
    }

    public void Remove(CashLedger row) => db.CashLedger.Remove(row);

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}