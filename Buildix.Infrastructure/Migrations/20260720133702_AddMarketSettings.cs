using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketSettings",
                columns: table => new
                {
                    MarketId = table.Column<int>(type: "integer", nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    WorkingHours = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SalesOnlyWhenShiftOpen = table.Column<bool>(type: "boolean", nullable: false),
                    CashWithdrawalNeedsApproval = table.Column<bool>(type: "boolean", nullable: false),
                    DebtOnlyForRegulars = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultDebtLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AllowedCashDiscrepancy = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShiftAutoCloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ReceiptHeader = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReceiptFooter = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AutoPrintReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultLanguage = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    FirstDayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    MinStockAlertEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    BlockSaleBelowCost = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultMarkupPct = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    NotifyDaySummary = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOverdueDebts = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyWithdrawalRequests = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerTelegram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OwnerTelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    InactivityLogoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    AuditEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketSettings", x => x.MarketId);
                    table.ForeignKey(
                        name: "FK_MarketSettings_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketSettings");
        }
    }
}
