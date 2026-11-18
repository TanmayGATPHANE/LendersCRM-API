using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutsourced : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOutsourced",
                table: "Entries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OutsourcedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    OutsourcedTo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OutsourceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsourcedItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItems_GroupId",
                table: "OutsourcedItems",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsourcedItems_OutsourceDate",
                table: "OutsourcedItems",
                column: "OutsourceDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutsourcedItems");

            migrationBuilder.DropColumn(
                name: "IsOutsourced",
                table: "Entries");
        }
    }
}
