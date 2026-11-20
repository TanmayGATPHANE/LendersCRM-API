using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AMS.Api.Repositories;

/// The database-side of entry deletion (§4.9). THE MOVE itself lives in
/// the database — a stored procedure (usp_Entries_MoveDeleted) invoked by
/// the trigger on Entries when IsDeleted flips to 1 — exactly as the
/// counter specified: safer, single-place SQL. The app's part: the
/// guardrails, the cash-row removal, the audit log, and handing the
/// trigger its audit context (who + why) through SESSION_CONTEXT.
public interface IArchiveRepository
{
    /// Begin the transaction the flag-set + cash removals commit inside.
    Task<IDbContextTransaction> BeginTransactionAsync();
    /// Hand the trigger its audit context: the acting user and the reason.
    Task SetDeletionContextAsync(int? userId, string? reason);
    /// Are any of these entries still in the working table? (Verifies the
    /// trigger actually moved them — a missing trigger must not hide.)
    Task<int> StillPresentAsync(IEnumerable<int> entryIds);
    Task<int> SaveAsync();
}

public sealed class ArchiveRepository(AmsDbContext db) : IArchiveRepository
{
    public Task<IDbContextTransaction> BeginTransactionAsync() =>
        db.Database.BeginTransactionAsync();

    /// SESSION_CONTEXT is per-connection — set inside the transaction, the
    /// stored procedure (fired by the trigger in the same save) reads it.
    public async Task SetDeletionContextAsync(int? userId, string? reason)
    {
        await db.Database.ExecuteSqlRawAsync(
            "EXEC sp_set_session_context @key = N'AmsUserId', @value = {0}; " +
            "EXEC sp_set_session_context @key = N'AmsDeleteReason', @value = {1}",
            userId?.ToString() ?? "",
            reason ?? "");
    }

    public Task<int> StillPresentAsync(IEnumerable<int> entryIds) =>
        db.Entries.AsNoTracking()
            .CountAsync(e => entryIds.Contains(e.Id));

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}
