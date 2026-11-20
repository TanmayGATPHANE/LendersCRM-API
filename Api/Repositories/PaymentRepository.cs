using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IPaymentRepository
{
    Task<List<PartialReturn>> ListAsync();
    Task<List<PartialReturn>> ListForGroupAsync(int groupId);
    /// Partial returns for several groups at once — the paged dashboard
    /// only needs the page's groups.
    Task<List<PartialReturn>> ListForGroupsAsync(List<int> groupIds);
    Task<PartialReturn?> FindByIdAsync(int id, bool tracked = true);
    Task AddAsync(PartialReturn payment);
    Task<int> SaveAsync();
}

public sealed class PaymentRepository(AmsDbContext db) : IPaymentRepository
{
    public Task<List<PartialReturn>> ListAsync() =>
        db.PartialReturns.AsNoTracking()
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .ToListAsync();

    public Task<List<PartialReturn>> ListForGroupAsync(int groupId) =>
        db.PartialReturns.AsNoTracking()
            .Where(p => p.GroupId == groupId)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .ToListAsync();

    public Task<List<PartialReturn>> ListForGroupsAsync(List<int> groupIds) =>
        db.PartialReturns.AsNoTracking()
            .Where(p => groupIds.Contains(p.GroupId))
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.Id)
            .ToListAsync();

    public Task<PartialReturn?> FindByIdAsync(int id, bool tracked = true) =>
        tracked
            ? db.PartialReturns.FirstOrDefaultAsync(p => p.Id == id)
            : db.PartialReturns.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    public Task AddAsync(PartialReturn payment)
    {
        db.PartialReturns.Add(payment);
        return Task.CompletedTask;
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}