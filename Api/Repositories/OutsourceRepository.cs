using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

/// The only layer touching the OutsourcedItems table (§4.8): one row per
/// round a collateral spends with an outside party. The open row
/// (ReturnedDate null) is the group's current out-of-house state.
public interface IOutsourceRepository
{
    Task AddAsync(OutsourcedItem item);
    Task<int> SaveAsync();
    /// The group's open round, or null when it is not currently outsourced.
    Task<OutsourcedItem?> FindOpenForGroupAsync(int groupId, bool tracked = false);
    /// Every OPEN round, oldest first — the Outsourced page's rows.
    Task<List<OutsourcedItem>> ListOpenAsync();
}

public sealed class OutsourceRepository(AmsDbContext db) : IOutsourceRepository
{
    public Task AddAsync(OutsourcedItem item)
    {
        db.OutsourcedItems.Add(item);
        return Task.CompletedTask;
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();

    public Task<OutsourcedItem?> FindOpenForGroupAsync(int groupId, bool tracked = false) =>
        tracked
            ? db.OutsourcedItems
                .Where(x => x.GroupId == groupId && x.ReturnedDate == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync()
            : db.OutsourcedItems.AsNoTracking()
                .Where(x => x.GroupId == groupId && x.ReturnedDate == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync();

    public Task<List<OutsourcedItem>> ListOpenAsync() =>
        db.OutsourcedItems.AsNoTracking()
            .Where(x => x.ReturnedDate == null)
            .OrderBy(x => x.OutsourceDate).ThenBy(x => x.Id)
            .ToListAsync();
}
