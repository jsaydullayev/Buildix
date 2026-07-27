using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformSettingsAndRenewalReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RenewalReminderSentFor",
                table: "Markets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    WarnOnOverdue = table.Column<bool>(type: "boolean", nullable: false),
                    RestrictAfterGrace = table.Column<bool>(type: "boolean", nullable: false),
                    FullBlockAfterDays = table.Column<int>(type: "integer", nullable: false),
                    SoonThresholdDays = table.Column<int>(type: "integer", nullable: false),
                    NotifyExpiring = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiryReminderDays = table.Column<int>(type: "integer", nullable: false),
                    SupportPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SupportTelegram = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupportEmail = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PlatformSettings",
                columns: new[] { "Id", "ExpiryReminderDays", "FullBlockAfterDays", "GraceDays", "NotifyBlocked", "NotifyExpiring", "RestrictAfterGrace", "SoonThresholdDays", "SupportEmail", "SupportPhone", "SupportTelegram", "UpdatedAtUtc", "WarnOnOverdue" },
                values: new object[] { 1, 3, 30, 5, true, true, true, 7, null, "+998 71 200 70 07", "@buildix_support", new DateTime(2026, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropColumn(
                name: "RenewalReminderSentFor",
                table: "Markets");
        }
    }
}
