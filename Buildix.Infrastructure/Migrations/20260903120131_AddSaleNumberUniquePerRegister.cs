using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleNumberUniquePerRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sales_MarketId_RegisterCode_SaleNumber",
                table: "Sales",
                columns: new[] { "MarketId", "RegisterCode", "SaleNumber" },
                unique: true,
                filter: "\"SaleNumber\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_MarketId_RegisterCode_SaleNumber",
                table: "Sales");
        }
    }
}
