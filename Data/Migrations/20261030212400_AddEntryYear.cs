using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EntryYear",
                table: "Entries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill every existing lend from its own date (idempotent) —
            // new rows get the year for free from AppDbContext.SaveChanges.
            migrationBuilder.Sql(
                "UPDATE Entries SET EntryYear = YEAR(EntryDate) WHERE EntryYear <> YEAR(EntryDate);");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_EntryYear",
                table: "Entries",
                column: "EntryYear");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Entries_EntryYear",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "EntryYear",
                table: "Entries");
        }
    }
}
