using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface ICashLedgerRepository
{
    Task<List<CashLedger>> ListAsync();
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
    public Task<List<CashLedger>> ListAsync() =>
        db.CashLedger.AsNoTracking()
            .OrderBy(x => x.LedgerDate).ThenBy(x => x.Id)
            .ToListAsync();

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