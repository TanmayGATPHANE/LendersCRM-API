using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class CashLedgerTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashLedger",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LedgerDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashLedger", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_LedgerDate",
                table: "CashLedger",
                column: "LedgerDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_ReferenceId",
                table: "CashLedger",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CashLedger_Source",
                table: "CashLedger",
                column: "Source");

            // Carry the manual drawer rows recorded before this table existed
            // over as 'Manual' rows — the CashLedger replaces CashAdjustments.
            migrationBuilder.Sql(
                @"
                INSERT INTO CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
                SELECT AdjustedDate, Amount, 'Manual', NULL, NULL, Notes, RecordedByUserId, CreatedAt
                FROM CashAdjustments;");

            // Backfill the money that moved before this table existed, so
            // SUM(Amount) ties with the books from day one. Partial returns
            // become their rows; each already-released collateral books the
            // two release entries (principal + interest), dated the release
            // day. NOT EXISTS guards keep re-runs from double-posting.
            migrationBuilder.Sql(
                @"
                INSERT INTO CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
                SELECT pr.PaymentDate, pr.Amount, 'PartialReturn', pr.GroupId, pr.Id, pr.Notes, pr.RecordedByUserId, pr.CreatedAt
                FROM PartialReturns pr
                WHERE NOT EXISTS (
                    SELECT 1 FROM CashLedger c
                    WHERE c.Source = 'PartialReturn' AND c.ReferenceId = pr.Id);

                INSERT INTO CashLedger (LedgerDate, Amount, Source, GroupId, ReferenceId, Notes, RecordedByUserId, CreatedAt)
                SELECT e.EntryDate, -e.AmountLend, 'Lend', e.GroupId, e.Id, NULL, e.CreatedByUserId, e.CreatedAt
                FROM Entries e
                WHERE NOT EXISTS (
                    SELECT 1 FROM CashLedger c
                    WHERE c.Source = 'Lend' AND c.ReferenceId = e.Id);

                INSERT INTO CashLedger (LedgerDate, Amount, Source, GroupId, Notes, CreatedAt)
                SELECT g.Rd, g.Principal, 'ReleasePrincipal', g.GroupId, NULL, SYSDATETIME()
                FROM (
                    SELECT GroupId, MAX(ReleasedDate) AS Rd,
                           SUM(COALESCE(AmountReturned, AmountLend) - COALESCE(InterestCollected, 0)) AS Principal
                    FROM Entries
                    WHERE Status = 'Released'
                    GROUP BY GroupId
                ) g
                WHERE NOT EXISTS (
                    SELECT 1 FROM CashLedger c
                    WHERE c.Source = 'ReleasePrincipal' AND c.GroupId = g.GroupId);

                INSERT INTO CashLedger (LedgerDate, Amount, Source, GroupId, Notes, CreatedAt)
                SELECT g.Rd, g.Interest, 'ReleaseInterest', g.GroupId, NULL, SYSDATETIME()
                FROM (
                    SELECT GroupId, MAX(ReleasedDate) AS Rd,
                           SUM(COALESCE(InterestCollected, 0)) AS Interest
                    FROM Entries
                    WHERE Status = 'Released'
                    GROUP BY GroupId
                ) g
                WHERE NOT EXISTS (
                    SELECT 1 FROM CashLedger c
                    WHERE c.Source = 'ReleaseInterest' AND c.GroupId = g.GroupId);");

            migrationBuilder.DropTable(
                name: "CashAdjustments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashLedger");

            migrationBuilder.CreateTable(
                name: "CashAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdjustedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashAdjustments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashAdjustments_AdjustedDate",
                table: "CashAdjustments",
                column: "AdjustedDate");
        }
    }
}
