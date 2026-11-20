using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IDeletionService
{
    Task DeleteEntryAsync(int entryId, string? reason, Actor actor);
    Task DeleteGroupAsync(int groupId, string? reason, Actor actor);
}

/** Deleting entries (§4.9) — a Writer remove for WRONG records.
    Division of labor, exactly as specified:
      DATABASE: the move. A stored procedure (usp_Entries_MoveDeleted),
      invoked by the Entries trigger the moment IsDeleted flips to 1,
      archives the row (full copy + who/why from SESSION_CONTEXT) and
      removes it from the working table — the ONLY code that moves.
      APP: everything else — the guardrails (released/recorded-returns
      409), removing the lend's −Lend cash row, the Activity audit row,
      and feeding the trigger its audit context.
      · RELEASED entries are read-only history — never deletable (409).
      · A collateral with RECORDED PARTIAL RETURNS cannot be deleted —
        money received stays in the books (409; release it instead).
    Deleting the group's LAST lend removes the whole collateral. */
public sealed class DeletionService(
    IEntryRepository entries, IPaymentRepository payments, ICashLedgerRepository cash,
    IArchiveRepository archive, IActivityRepository activity) : IDeletionService
{
    public async Task DeleteEntryAsync(int entryId, string? reason, Actor actor) =>
        await DeleteAsync(await LoadEntryAsync(entryId), reason, actor, wholeGroup: false);

    public async Task DeleteGroupAsync(int groupId, string? reason, Actor actor) =>
        await DeleteAsync(await LoadGroupAsync(groupId), reason, actor, wholeGroup: true);

    // ── the shared removal ────────────────────────────────────────────────

    private async Task DeleteAsync(List<Entry> targets, string? reason, Actor actor, bool wholeGroup)
    {
        await using var tx = await archive.BeginTransactionAsync();

        // The app's part — the cash table keeps its story straight: the
        // lend's −row goes out with the lend it was booked from.
        foreach (var entry in targets)
        {
            var ledgerRow = await cash.FindForSourceAsync("Lend", entry.Id, tracked: true);
            if (ledgerRow is not null) cash.Remove(ledgerRow);
        }

        // Hand the trigger its audit context (who + why), then mark the
        // rows. THE MOVE itself is database-side: the UPDATE fires the
        // Entries trigger, which runs usp_Entries_MoveDeleted — the row
        // is archived to EntriesDeleted (full copy + who/when) and
        // removed from the working table in the very statement.
        await archive.SetDeletionContextAsync(actor.Id, reason);
        foreach (var entry in targets) entry.IsDeleted = true;
        await archive.SaveAsync();

        // The trigger is what makes this work — if it is not installed on
        // this database, the flag would sit there silently and the rows
        // would never move. Verify + refuse rather than pretend.
        var ids = targets.Select(e => e.Id).ToList();
        if (await archive.StillPresentAsync(ids) > 0)
        {
            await tx.RollbackAsync();
            throw ApiException.Conflict(
                "The deletion trigger is not installed on this database — " +
                "run datamod/03-entry-move-trigger.sql, then delete again.");
        }

        await tx.CommitAsync();

        // The audit row: what left the books, and to where.
        var first = targets.OrderBy(e => (e.EntryDate, e.Id)).First();
        await activity.AddAsync(actor, "Deleted", wholeGroup ? "Group" : "Entry", first.Id, first.GroupId,
            $"Deleted entry — {first.ItemDescription} ({ActivityStages.Money(targets.Sum(e => e.AmountLend))}, " +
            $"{first.Customer?.Name ?? $"customer #{first.CustomerId}"}), archived by the Entries trigger");
        await activity.SaveAsync();
    }

    private async Task<List<Entry>> LoadEntryAsync(int entryId)
    {
        var entry = await entries.FindByIdAsync(entryId, tracked: true)
            ?? throw ApiException.NotFound("Entry not found");
        if (entry.Status == "Released")
            throw ApiException.Conflict("Released entries are read-only history — they cannot be deleted");
        await RefuseIfPaymentsAsync(entry.GroupId);
        return [entry];
    }

    private async Task<List<Entry>> LoadGroupAsync(int groupId)
    {
        var group = await entries.ListForGroupAsync(groupId, tracked: true);
        if (group.Count == 0) throw ApiException.NotFound("Collateral group not found");
        if (group.Any(e => e.Status == "Released"))
            throw ApiException.Conflict("Released collaterals are read-only history — they cannot be deleted");
        await RefuseIfPaymentsAsync(groupId);
        return group;
    }

    /** Money received against the collateral kills the delete: the books
        keep what the counter collected (the release flow settles it). */
    private async Task RefuseIfPaymentsAsync(int groupId)
    {
        if ((await payments.ListForGroupAsync(groupId)).Count > 0)
            throw ApiException.Conflict(
                "This collateral has recorded partial returns — money received stays in the books, so it cannot be deleted. Release it instead.");
    }
}
