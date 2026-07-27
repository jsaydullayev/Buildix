using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramLinkCodes : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Telegram bog'lanishini tasdiqlash. Ilgari foydalanuvchi xom chat ID
        /// yozardi va egaligini hech narsa tekshirmasdi; endi bog'lanish faqat
        /// botning bir martalik kodi bilan o'rnatiladi. Mavjud bog'lanishlar
        /// tegilmaydi — Users.TelegramChatId o'z joyida qoladi.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TelegramLinkAttempts",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramLinkAttemptsResetUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TelegramLinkCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramLinkCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkCodes_ChatId",
                table: "TelegramLinkCodes",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkCodes_Code",
                table: "TelegramLinkCodes",
                column: "Code",
                unique: true,
                filter: "\"UsedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkCodes_ExpiresAtUtc",
                table: "TelegramLinkCodes",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramLinkCodes");

            migrationBuilder.DropColumn(
                name: "TelegramLinkAttempts",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramLinkAttemptsResetUtc",
                table: "Users");
        }
    }
}
