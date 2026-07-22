using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashShiftReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CountedCash",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discrepancy",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCash",
                table: "Shifts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReconStatus",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "CashWithdrawals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "CashWithdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "CashWithdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "CashWithdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "CashWithdrawals",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountedCash",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "Discrepancy",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "OpeningCash",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ReconStatus",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "CashWithdrawals");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "CashWithdrawals");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "CashWithdrawals");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "CashWithdrawals");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "CashWithdrawals");
        }
    }
}
