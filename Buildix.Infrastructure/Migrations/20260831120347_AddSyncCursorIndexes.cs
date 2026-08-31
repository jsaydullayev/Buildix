using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncCursorIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Zakups_UpdatedAt_Id",
                table: "Zakups",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ZakupReceipts_UpdatedAt_Id",
                table: "ZakupReceipts",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_UpdatedAt_Id",
                table: "Users",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_UpdatedAt_Id",
                table: "Suppliers",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_UpdatedAt_Id",
                table: "StockMovements",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Shifts_UpdatedAt_Id",
                table: "Shifts",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_UpdatedAt_Id",
                table: "Sales",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_UpdatedAt_Id",
                table: "SaleReturns",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnItems_UpdatedAt_Id",
                table: "SaleReturnItems",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_UpdatedAt_Id",
                table: "SaleItems",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_UpdatedAt_Id",
                table: "Products",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UpdatedAt_Id",
                table: "Payments",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Debts_UpdatedAt_Id",
                table: "Debts",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UpdatedAt_Id",
                table: "Customers",
                columns: new[] { "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_UpdatedAt_Id",
                table: "CashMovements",
                columns: new[] { "UpdatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Zakups_UpdatedAt_Id",
                table: "Zakups");

            migrationBuilder.DropIndex(
                name: "IX_ZakupReceipts_UpdatedAt_Id",
                table: "ZakupReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Users_UpdatedAt_Id",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_UpdatedAt_Id",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_UpdatedAt_Id",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Shifts_UpdatedAt_Id",
                table: "Shifts");

            migrationBuilder.DropIndex(
                name: "IX_Sales_UpdatedAt_Id",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleReturns_UpdatedAt_Id",
                table: "SaleReturns");

            migrationBuilder.DropIndex(
                name: "IX_SaleReturnItems_UpdatedAt_Id",
                table: "SaleReturnItems");

            migrationBuilder.DropIndex(
                name: "IX_SaleItems_UpdatedAt_Id",
                table: "SaleItems");

            migrationBuilder.DropIndex(
                name: "IX_Products_UpdatedAt_Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Payments_UpdatedAt_Id",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Debts_UpdatedAt_Id",
                table: "Debts");

            migrationBuilder.DropIndex(
                name: "IX_Customers_UpdatedAt_Id",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_UpdatedAt_Id",
                table: "CashMovements");
        }
    }
}
