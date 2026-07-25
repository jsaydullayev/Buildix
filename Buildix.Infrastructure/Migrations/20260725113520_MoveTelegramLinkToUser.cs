using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveTelegramLinkToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ORDER MATTERS. The scaffolded version dropped the old columns first,
            // which would have thrown away every owner's existing bot link. The
            // new column is added first, the links are carried over, and only then
            // are the old columns dropped.
            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "Users",
                type: "bigint",
                nullable: true);

            // Carry each market's linked chat to that market's Owner. Role 1 =
            // Owner (Role enum is a DB contract). DISTINCT ON keeps one row per
            // chat id: the same Telegram account could have owned two markets,
            // and the unique index below would reject the duplicate.
            migrationBuilder.Sql("""
                UPDATE "Users" u
                SET "TelegramChatId" = pick."ChatId"
                FROM (
                    SELECT DISTINCT ON (s."OwnerTelegramChatId")
                           s."OwnerTelegramChatId" AS "ChatId", o."Id" AS "UserId"
                    FROM "MarketSettings" s
                    JOIN "Users" o
                      ON o."MarketId" = s."MarketId"
                     AND o."Role" = 1
                     AND o."IsDeleted" = false
                    WHERE s."OwnerTelegramChatId" IS NOT NULL
                    ORDER BY s."OwnerTelegramChatId", o."CreatedAt"
                ) AS pick
                WHERE u."Id" = pick."UserId";
                """);

            migrationBuilder.DropColumn(
                name: "OwnerTelegram",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "OwnerTelegramChatId",
                table: "MarketSettings");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TelegramChatId",
                table: "Users",
                column: "TelegramChatId",
                unique: true,
                filter: "\"TelegramChatId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_TelegramChatId",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "OwnerTelegram",
                table: "MarketSettings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OwnerTelegramChatId",
                table: "MarketSettings",
                type: "bigint",
                nullable: true);

            // Carry owners' links back so a rollback doesn't silently unlink every
            // shop. Non-owner links have no home in the old shape and are dropped.
            migrationBuilder.Sql("""
                UPDATE "MarketSettings" s
                SET "OwnerTelegramChatId" = o."TelegramChatId"
                FROM "Users" o
                WHERE o."MarketId" = s."MarketId"
                  AND o."Role" = 1
                  AND o."IsDeleted" = false
                  AND o."TelegramChatId" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "Users");
        }
    }
}
