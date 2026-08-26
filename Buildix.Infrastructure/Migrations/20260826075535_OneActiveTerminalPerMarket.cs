using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OneActiveTerminalPerMarket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopTerminals_MarketId",
                table: "ShopTerminals");

            migrationBuilder.CreateIndex(
                name: "IX_ShopTerminals_ActivePerMarket",
                table: "ShopTerminals",
                column: "MarketId",
                unique: true,
                filter: "\"RevokedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopTerminals_ActivePerMarket",
                table: "ShopTerminals");

            migrationBuilder.CreateIndex(
                name: "IX_ShopTerminals_MarketId",
                table: "ShopTerminals",
                column: "MarketId");
        }
    }
}
