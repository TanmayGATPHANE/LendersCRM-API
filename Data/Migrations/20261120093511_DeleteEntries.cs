using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeleteEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Entries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EntriesDeleted",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    EntryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EntryYear = table.Column<int>(type: "int", nullable: false),
                    ItemDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AmountLend = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    InterestRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ExpectedReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AmountReturned = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    InterestCollected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    TotalCollected = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    ReleasedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsOutsourced = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true),
                    DeleteReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntriesDeleted", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntriesDeleted_DeletedAtUtc",
                table: "EntriesDeleted",
                column: "DeletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EntriesDeleted_GroupId",
                table: "EntriesDeleted",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntriesDeleted");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Entries");
        }
    }
}
