using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleItemAndZakupProductSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "Zakups",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "SaleItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProductUnit",
                table: "SaleItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // ── ESKI YOZUVLARNI TO'LDIRISH ───────────────────────────────
            //
            // Ustunlar bo'sh qo'shilsa, ko'chirishdan keyingi lahzada BUTUN
            // eski tarix nomsiz bo'lib qolardi: yangi kod nomni qatorning
            // o'zidan o'qiydi va u yerda bo'sh satr turardi. Shuning uchun
            // to'ldirish ustun qo'shilishi bilan BIR ko'chirishda, ya'ni
            // bitta tranzaksiyada bajariladi — oraliq holat umuman
            // ko'rinmaydi.
            //
            // `Products` ga to'g'ridan-to'g'ri murojaat qilinadi: xom SQL
            // EF ning yumshoq-o'chirish filtridan o'tmaydi, ya'ni ALLAQACHON
            // o'chirilgan tovarlarning nomi ham tiklanadi — aynan shular
            // uchun bu ish qilinyapti.

            // Oddiy qatorlar — nom va birlik tovardan.
            migrationBuilder.Sql("""
                UPDATE "SaleItems" si
                SET "ProductName" = p."Name",
                    "ProductUnit" = p."Unit"
                FROM "Products" p
                WHERE p."Id" = si."ProductId"
                  AND si."IsExternal" = false;
                """);

            // Tashqi (katalogda yo'q) qatorlar — nom o'zida turadi.
            migrationBuilder.Sql("""
                UPDATE "SaleItems"
                SET "ProductName" = COALESCE("ExternalProductName", '')
                WHERE "IsExternal" = true;
                """);

            // Priyomka qatorlari.
            migrationBuilder.Sql("""
                UPDATE "Zakups" z
                SET "ProductName" = p."Name"
                FROM "Products" p
                WHERE p."Id" = z."ProductId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "Zakups");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "ProductUnit",
                table: "SaleItems");
        }
    }
}
