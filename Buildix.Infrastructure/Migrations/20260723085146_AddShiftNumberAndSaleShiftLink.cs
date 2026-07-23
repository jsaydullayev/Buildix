using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftNumberAndSaleShiftLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftNumber",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_MarketId_ShiftNumber",
                table: "Shifts",
                columns: new[] { "MarketId", "ShiftNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ShiftId",
                table: "Sales",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Shifts_ShiftId",
                table: "Sales",
                column: "ShiftId",
                principalTable: "Shifts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Shifts_ShiftId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_MarketId_ShiftNumber",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Sales_ShiftId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ShiftNumber",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "Sales");
        }
    }
}
