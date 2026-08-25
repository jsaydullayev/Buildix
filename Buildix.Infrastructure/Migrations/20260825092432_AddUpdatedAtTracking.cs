using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "PlatformSettings",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedAtUtc",
                table: "PlatformPlans",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "LastUpdated",
                table: "CashRegisters",
                newName: "UpdatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_CashRegisters_LastUpdated",
                table: "CashRegisters",
                newName: "IX_CashRegisters_UpdatedAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Zakups",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ZakupReceipts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TelegramLinkCodes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Suppliers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SubscriptionPayments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "StockMovements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Shifts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SaleReturns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SaleReturnItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SaleItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RegistrationRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RefreshTokens",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Markets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "LoginHistories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Debts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "CashWithdrawals",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "CashMovements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // ── Mavjud qatorlarni to'ldirish ────────────────────────────────
            // EF yangi ustunga DateTime.MinValue qo'yadi. Uni shundayligicha
            // qoldirib bo'lmaydi: bu maydon bulut bilan sinxronizatsiyaning suv
            // belgisi va u har bir eski qatorni «hech qachon o'zgarmagan» deb
            // ko'rsatardi.
            //
            // CreatedAt olinadi, «hozir» EMAS. «Hozir» qo'yilsa butun baza bir
            // vaqtning o'zida o'zgargandek ko'rinardi va birinchi
            // sinxronizatsiyadan keyin ham qaysi yozuv haqiqatan yangi ekanini
            // ajratib bo'lmasdi. CreatedAt esa rost: yozuv o'shanda paydo
            // bo'lgan va shundan beri (bizga ma'lum darajada) o'zgarmagan.
            //
            // Shart '-infinity' bo'yicha. Npgsql DateTime.MinValue ni
            // timestamptz ustunga aynan shunday yozadi — sanani solishtirgan
            // birinchi variant hech qanday qatorga tushmagan va migratsiya
            // JIMGINA hech narsa qilmagan edi. Ikkalasi ham tekshiriladi.
            //
            // AuditLogs va DebtAuditLogs ro'yxatda YO'Q: ularda UPDATE ni rad
            // etadigan trigger bor va ularga bu ustun umuman qo'shilmaydi
            // (sabab — AppDbContext dagi izoh).
            foreach (var table in new[]
            {
                "CashMovements", "CashWithdrawals", "Customers", "Debts", "LoginHistories",
                "Markets", "Notifications", "Payments", "RefreshTokens", "RegistrationRequests",
                "SaleItems", "SaleReturnItems", "SaleReturns", "Sales", "Shifts", "StockMovements",
                "SubscriptionPayments", "Suppliers", "TelegramLinkCodes", "Users", "ZakupReceipts",
                "Zakups",
            })
            {
                migrationBuilder.Sql(
                    $"""
                     UPDATE "{table}"
                     SET "UpdatedAt" = "CreatedAt"
                     WHERE "UpdatedAt" = '-infinity'::timestamptz
                        OR "UpdatedAt" = '0001-01-01 00:00:00+00'::timestamptz;
                     """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Zakups");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ZakupReceipts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TelegramLinkCodes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RegistrationRequests");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Markets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "LoginHistories");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Debts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CashWithdrawals");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CashMovements");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "PlatformSettings",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "PlatformPlans",
                newName: "UpdatedAtUtc");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "CashRegisters",
                newName: "LastUpdated");

            migrationBuilder.RenameIndex(
                name: "IX_CashRegisters_UpdatedAt",
                table: "CashRegisters",
                newName: "IX_CashRegisters_LastUpdated");
        }
    }
}
