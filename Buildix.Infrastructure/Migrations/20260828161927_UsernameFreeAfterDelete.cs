using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UsernameFreeAfterDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_MarketId_Username_Unique",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username_GlobalUnique",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_MarketId_Username_Unique",
                table: "Users",
                columns: new[] { "MarketId", "Username" },
                unique: true,
                filter: "\"MarketId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username_GlobalUnique",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "\"MarketId\" IS NULL AND NOT \"IsDeleted\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_MarketId_Username_Unique",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username_GlobalUnique",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_MarketId_Username_Unique",
                table: "Users",
                columns: new[] { "MarketId", "Username" },
                unique: true,
                filter: "\"MarketId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username_GlobalUnique",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "\"MarketId\" IS NULL");
        }
    }
}
