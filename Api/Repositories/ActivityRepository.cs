using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IActivityRepository
{
    /// The audit trail (newest first) — optionally clipped to one user's
    /// trail or one collateral group's history, capped so the tab can
    /// never pull a runaway log.
    Task<List<ActivityLog>> ListAsync(int? userId = null, int? groupId = null, int take = 2000);
    /// Stage a row (call RecordAsync to stage + save in one go).
    Task AddAsync(ActivityLog row);
    Task<int> SaveAsync();
}

/// The only layer touching the ActivityLog table. Shares the scoped
/// DbContext like every other repository, so a service can stage its row
/// and the business save can still go through its own repository unchanged.
public sealed class ActivityRepository(AmsDbContext db) : IActivityRepository
{
    public async Task<List<ActivityLog>> ListAsync(int? userId, int? groupId, int take)
    {
        var q = db.ActivityLog.AsNoTracking().AsQueryable();
        if (userId is { } uid) q = q.Where(x => x.UserId == uid);
        if (groupId is { } gid) q = q.Where(x => x.GroupId == gid);
        return await q
            .OrderByDescending(x => x.AtUtc).ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync();
    }

    public Task AddAsync(ActivityLog row)
    {
        db.ActivityLog.Add(row);
        return Task.CompletedTask;
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}