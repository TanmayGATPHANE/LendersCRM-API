using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class EntryMoveTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // §4.9 — the database-side move: the stored procedure (the ONLY
            // mover of entries into EntriesDeleted) + the Entries trigger
            // that fires it when IsDeleted flips to 1. Same content runs on
            // script-created databases via datamod/03-entry-move-trigger.sql.
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'usp_Entries_MoveDeleted', N'P') IS NULL
    EXEC(N'CREATE PROCEDURE usp_Entries_MoveDeleted AS SET NOCOUNT ON;');
GO
ALTER PROCEDURE usp_Entries_MoveDeleted
AS
BEGIN
    SET NOCOUNT ON;

    -- The audit context the app handed over (SESSION_CONTEXT is
    -- connection-scoped, set inside the same transaction as the flag).
    DECLARE @UserId  int = TRY_CAST(SESSION_CONTEXT(N'AmsUserId') AS int);
    DECLARE @Reason  nvarchar(500) = CAST(COALESCE(SESSION_CONTEXT(N'AmsDeleteReason'), N'') AS nvarchar(500));
    DECLARE @Now     datetime2 = SYSUTCDATETIME();

    -- Archive every flagged entry not already archived (id-guarded, so
    -- no double copies even if the flag is marked while a copy exists).
    INSERT INTO EntriesDeleted
        (Id, GroupId, CustomerId, EntryDate, EntryYear, ItemDescription,
         AmountLend, InterestRate, ExpectedReturn, Status, AmountReturned,
         InterestCollected, TotalCollected, ReleasedDate, Notes,
         IsOutsourced, CreatedByUserId, CreatedAt, ModifiedAt,
         DeletedAtUtc, DeletedByUserId, DeleteReason)
    SELECT E.Id, E.GroupId, E.CustomerId, E.EntryDate, E.EntryYear, E.ItemDescription,
           E.AmountLend, E.InterestRate, E.ExpectedReturn, E.Status, E.AmountReturned,
           E.InterestCollected, E.TotalCollected, E.ReleasedDate, E.Notes,
           E.IsOutsourced, E.CreatedByUserId, E.CreatedAt, E.ModifiedAt,
           @Now, @UserId, NULLIF(@Reason, N'')
    FROM Entries E
    WHERE E.IsDeleted = 1
      AND NOT EXISTS (SELECT 1 FROM EntriesDeleted D WHERE D.Id = E.Id);

    -- The move's second half: flagged rows leave the working table.
    DELETE E
    FROM Entries E
    INNER JOIN EntriesDeleted D ON D.Id = E.Id
    WHERE E.IsDeleted = 1;
END
GO
IF OBJECT_ID(N'trg_Entries_MoveDeleted', N'TR') IS NULL
    EXEC(N'CREATE TRIGGER trg_Entries_MoveDeleted ON Entries AFTER UPDATE AS SET NOCOUNT ON;');
GO
ALTER TRIGGER trg_Entries_MoveDeleted
ON Entries
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Fire ONLY when the flag FLIPS (0 → 1): a saved update that merely
    -- touches the row without deleting must not move it.
    IF EXISTS (
        SELECT 1
        FROM inserted i
        INNER JOIN deleted d ON d.Id = i.Id
        WHERE i.IsDeleted = 1 AND d.IsDeleted = 0
    )
        EXEC usp_Entries_MoveDeleted;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'trg_Entries_MoveDeleted', N'TR') IS NOT NULL
    DROP TRIGGER trg_Entries_MoveDeleted;
IF OBJECT_ID(N'usp_Entries_MoveDeleted', N'P') IS NOT NULL
    DROP PROCEDURE usp_Entries_MoveDeleted;
");
        }
    }
}
