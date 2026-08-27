using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaleOpeningBalanceFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "Sales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ── Mavjud yozuvlarni belgilaymiz ────────────────────────────────
            // Belgi endi paydo bo'ldi, lekin eski qarzning texnik qatorlari
            // bazada allaqachon bor va ular hisobotlarni buzib turibdi. Ularni
            // topish mumkin: haqiqiy savdoda HAR DOIM tovar qatori bo'ladi,
            // eski qarz qatorida esa umuman yo'q.
            //
            // Uchta shart birga tekshiriladi, chunki har biri yolg'iz o'zi
            // kam: tovarsiz qoralama ham bo'ladi (Status=0), tovarsiz bekor
            // qilingan chek ham (Status=4). Faqat qarzga tegishli statusdagi,
            // tovari yo'q va ortida qarz yozuvi turgan qator — bu aynan o'sha
            // texnik qator.
            //
            // Status 2 (Qarz) VA 3 (Yopilgan) — ikkalasi ham olinadi: eski
            // qarz to'lab bo'linganda qator Yopilgan'ga o'tadi. Faqat 2 ni
            // olsak, do'kon qancha uzoq ishlagan bo'lsa, shuncha ko'p eski
            // qator belgisiz qolardi — ya'ni tuzatish aynan eng eski
            // hisobotlarga yetib bormasdi. Boshqa statuslar bo'lishi mumkin
            // emas: bu qator Qarz bo'lib tug'iladi va to'lovdan keyin
            // faqat Yopilgan bo'ladi.
            migrationBuilder.Sql(@"
                UPDATE ""Sales"" s
                   SET ""IsOpeningBalance"" = true
                 WHERE s.""Status"" IN (2, 3)
                   AND NOT EXISTS (SELECT 1 FROM ""SaleItems"" si WHERE si.""SaleId"" = s.""Id"")
                   AND EXISTS (SELECT 1 FROM ""Debts"" d WHERE d.""SaleId"" = s.""Id"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ustun tushadi — belgilangan qatorlar yana oddiy savdoga
            // aylanadi. Ma'lumot yo'qolmaydi, faqat hisobotlar avvalgi
            // (noto'g'ri) holatiga qaytadi.
            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "Sales");
        }
    }
}
