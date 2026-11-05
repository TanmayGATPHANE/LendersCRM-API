using AMS.Api.Repositories;
using AMS.Data;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IActivityService
{
    /// The audit trail (newest first) — the whole log, one user's trail
    /// (userId) or one collateral group's history (groupId). Admin-only
    /// at the controller.
    Task<List<ActivityItem>> ListAsync(int? userId = null, int? groupId = null);
}

/// The Activity tab's read side (§10). WRITES happen in the business
/// services — each one stages its audit row with the same SaveChanges that
/// commits the business change, so a row exists only if the change exists.
public sealed class ActivityService(IActivityRepository activity) : IActivityService
{
    public async Task<List<ActivityItem>> ListAsync(int? userId, int? groupId)
    {
        var list = await activity.ListAsync(userId, groupId);
        return list.Select(ToItem).ToList();
    }

    private static ActivityItem ToItem(ActivityLog x) =>
        new(x.Id, x.AtUtc, x.UserId, x.UserName, x.Action, x.Entity, x.EntityId, x.GroupId, x.Details);
}

/// Shared staging helper for the writing services — ONE sentence builder so
/// every audit detail reads the same way across the app.
public static class ActivityStages
{
    /// Stage a row on the activity repo (id + name from the Actor); the
    /// caller commits it with its own SaveAsync.
    public static Task AddAsync(
        this IActivityRepository activity, Actor actor,
        string action, string entity, int entityId, int? groupId, string details)
    {
        return activity.AddAsync(new ActivityLog
        {
            UserId = actor.Id,
            UserName = actor.Name,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            GroupId = groupId,
            Details = details,
        });
    }

    /// ₹ formatting for the detail sentences (no paise noise on round rows).
    public static string Money(decimal v) =>
        v == Math.Truncate(v) ? $"₹{v:N0}" : $"₹{v:N2}";
}