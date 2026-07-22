using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiptNumber",
                table: "ZakupReceipts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ZakupReceipts_MarketId_ReceiptNumber",
                table: "ZakupReceipts",
                columns: new[] { "MarketId", "ReceiptNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZakupReceipts_MarketId_ReceiptNumber",
                table: "ZakupReceipts");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "ZakupReceipts");
        }
    }
}
