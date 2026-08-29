using System.Text;
using Buildix.Application.Services.Printing;
using Buildix.Application.Services.Reports;

namespace Buildix.Tests;

/// <summary>
/// Chek termal printerning o'z tilida (ESC/POS).
///
/// <para>Bu sinovlar qog'ozdan chiqqan chekni ko'rgandan keyin yozildi:
/// nom va summa bir-biriga yopishib «1 x 380 000380 000» bo'lib chiqqan,
/// qatorlar orasida joy qolmagan edi. Ustunlarni endi kod hisoblaydi,
/// ya'ni buni bir marta to'g'rilab, shu yerda qat'iy belgilash mumkin.</para>
/// </summary>
public class EscPosReceiptTests
{
    static EscPosReceiptTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static ReportPdfRenderer.InvoiceData Sale(
        decimal remaining = 0, decimal discount = 0, string customer = "") => new(
        MarketName: "Taxtapul stroy",
        MarketDescription: "Qurilish mollari",
        SellerName: "Jaxongir",
        CustomerName: customer,
        InvoiceNumber: Guid.Parse("9bbb1800-0000-4000-8000-000000000000"),
        SaleNumber: 21,
        Date: new DateTime(2026, 8, 28, 22, 3, 0, DateTimeKind.Utc),
        PaymentType: "Naqd",
        Items:
        [
            new ReportPdfRenderer.InvoiceItemData("DSP", 1, 380_000, 380_000, null, false),
            new ReportPdfRenderer.InvoiceItemData("taxta", 1, 70_000, 70_000, null, false),
            new ReportPdfRenderer.InvoiceItemData("sement", 1, 1_000, 1_000, null, false),
        ],
        TotalAmount: 451_000 - discount,
        PaidAmount: 451_000 - discount - remaining,
        RemainingAmount: remaining,
        Status: "Paid",
        SubtotalAmount: 451_000,
        DiscountAmount: discount);

    /// <summary>Baytlarni o'qiladigan qatorlarga aylantiradi (buyruqlarsiz).</summary>
    private static string[] Lines(byte[] bytes)
    {
        var text = Encoding.GetEncoding(866).GetString(bytes);
        // ESC/POS buyruqlari boshqaruv belgilaridan boshlanadi — ularni
        // tashlaymiz, qog'ozga chiqadigan matn qoladi.
        var clean = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\x1B' || c == '\x1D') { i += CommandLength(text, i) - 1; continue; }
            clean.Append(c);
        }
        return clean.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Shu sinovlarda ishlatiladigan buyruqlarning uzunligi.</summary>
    private static int CommandLength(string text, int at)
    {
        if (text[at] == '\x1B')
        {
            var next = at + 1 < text.Length ? text[at + 1] : '\0';
            return next == '@' ? 2 : 3;      // ESC @ | ESC a/E/t n
        }
        return at + 1 < text.Length && text[at + 1] == 'V' ? 4 : 3;   // GS V m n | GS ! n
    }

    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 48)]
    public void Qator_eni_rulonga_mos(int widthMm, int cols)
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", widthMm));

        // Ajratuvchi chiziqlar aynan bir qatorni to'ldiradi.
        var rules = lines.Where(l => l.All(c => c == '=' || c == '-') && l.Length > 3).ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Equal(cols, r.Length));
    }

    /// <summary>
    /// ASOSIY tuzatish: nom va summa bir-biriga YOPISHMAYDI.
    ///
    /// <para>Qog'ozdan «1 x 380 000380 000» chiqqan edi — ikkalasi orasida
    /// bo'shliq umuman yo'q edi va chekni o'qib bo'lmasdi.</para>
    /// </summary>
    [Fact]
    public void Nom_va_summa_orasida_joy_bor()
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", 80));

        var row = Assert.Single(lines.Where(l => l.TrimStart().StartsWith("1 x 380 000")));
        Assert.EndsWith("380 000", row);
        Assert.Contains("  ", row);   // orada bo'shliq bor
        Assert.DoesNotContain("380 000380 000", row);
    }

    /// <summary>Do'kon nomidan keyin uzun chiziq — chek shu yerdan boshlanadi.</summary>
    [Fact]
    public void Dokon_nomidan_keyin_uzun_chiziq()
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", 80));

        var nameAt = Array.FindIndex(lines, l => l.Contains("Taxtapul stroy"));
        Assert.True(nameAt >= 0, "do'kon nomi topilmadi");

        // Nomdan keyingi bir-ikki qator ichida to'liq chiziq bo'lishi kerak.
        var after = lines.Skip(nameAt + 1).Take(2);
        Assert.Contains(after, l => l.Length == 48 && l.All(c => c == '='));
    }

    [Fact]
    public void Har_bir_tovar_va_yakun_bor()
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", 80));
        var all = string.Join("\n", lines);

        Assert.Contains("DSP", all);
        Assert.Contains("taxta", all);
        Assert.Contains("sement", all);
        Assert.Contains("JAMI", all);
        Assert.Contains("451 000", all);
    }

    /// <summary>Qarz qolgan chekda u alohida qator bo'lib chiqadi.</summary>
    [Fact]
    public void Qarz_korsatiladi()
    {
        var all = string.Join("\n", Lines(EscPosReceipt.Build(Sale(remaining: 51_000), "uz", 80)));

        Assert.Contains("Qarz", all);
        Assert.Contains("51 000", all);
    }

    [Fact]
    public void Chegirma_korsatiladi()
    {
        var all = string.Join("\n", Lines(EscPosReceipt.Build(Sale(discount: 3_000), "uz", 80)));

        Assert.Contains("Chegirma", all);
        Assert.Contains("448 000", all);   // 451 000 - 3 000
    }

    /// <summary>
    /// Chek QIRQILADI — buni rasm yo'li umuman qila olmaydi.
    /// </summary>
    [Fact]
    public void Oxirida_qogoz_qirqiladi()
    {
        var bytes = EscPosReceipt.Build(Sale(), "uz", 80);

        // GS V 65 n
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x41 }, bytes[^4..^1]);
    }

    /// <summary>Boshida printer holati tozalanadi — oldingi chekdan qolgan sozlama qolmasin.</summary>
    [Fact]
    public void Boshida_printer_tozalanadi()
    {
        var bytes = EscPosReceipt.Build(Sale(), "uz", 80);

        Assert.Equal(new byte[] { 0x1B, 0x40 }, bytes[..2]);
    }

    /// <summary>
    /// Tipografik apostrof CP866 da yo'q va printer uning o'rniga tasodifiy
    /// belgi bosardi.
    /// </summary>
    [Fact]
    public void Tipografik_apostrof_almashtiriladi()
    {
        var data = Sale() with { MarketName = "Do’kon “Sement”" };

        var all = string.Join("\n", Lines(EscPosReceipt.Build(data, "uz", 80)));

        Assert.Contains("Do'kon", all);
        Assert.DoesNotContain('’', all);
    }

    /// <summary>Rus tilida yakun so'zlari ham ruscha chiqadi.</summary>
    [Fact]
    public void Rus_tilida_chiqadi()
    {
        var all = string.Join("\n", Lines(EscPosReceipt.Build(Sale(), "ru", 80)));

        Assert.Contains("ИТОГО", all);
        Assert.Contains("Чек", all);
    }

    /// <summary>
    /// Uzun tovar nomi summani QIRQIB yubormaydi — chekdagi eng muhim son
    /// o'sha.
    /// </summary>
    [Fact]
    public void Uzun_nom_summani_qirqmaydi()
    {
        var data = Sale() with
        {
            Items =
            [
                new ReportPdfRenderer.InvoiceItemData(
                    new string('X', 200), 1, 380_000, 380_000, null, false),
            ],
        };

        var lines = Lines(EscPosReceipt.Build(data, "uz", 58));

        Assert.All(lines, l => Assert.True(l.Length <= 32, $"qator {l.Length} belgidan iborat"));
        Assert.Contains(lines, l => l.EndsWith("380 000"));
    }

    /// <summary>
    /// JAMI ikki baravar kattalikda bosiladi, ya'ni qatorga ikki barobar
    /// KAM belgi sig'adi.
    /// </summary>
    /// <remarks>
    /// To'liq en bo'yicha to'ldirilsa summa qog'ozdan chiqib ketardi —
    /// ustunlar hisobi kattalik bilan birga o'zgarishi shart.
    /// </remarks>
    [Theory]
    [InlineData(58, 16)]
    [InlineData(80, 24)]
    public void Jami_kattaligiga_yarasha_tekislanadi(int widthMm, int cols)
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", widthMm));

        var jami = Assert.Single(lines, l => l.StartsWith("JAMI"));
        Assert.Equal(cols, jami.Length);
        Assert.EndsWith("451 000", jami);
    }

    /// <summary>
    /// Butun chek QALIN bosiladi — termal bosh ingichka va och yozadi va
    /// do'kon yorug'ida chekni o'qib bo'lmasdi.
    /// </summary>
    [Fact]
    public void Butun_chek_qalin_bosiladi()
    {
        var bytes = EscPosReceipt.Build(Sale(), "uz", 80);

        var on = Find(bytes, [0x1B, 0x45, 0x01]);   // ESC E 1
        var off = Find(bytes, [0x1B, 0x45, 0x00]);  // ESC E 0

        Assert.True(on >= 0, "qalin rejim yoqilmagan");
        // Do'kon nomidan OLDIN yoqiladi va faqat oxirida o'chadi.
        Assert.True(on < Find(bytes, Encoding.GetEncoding(866).GetBytes("Taxtapul")));
        Assert.True(off > on, "qalin rejim o'chirilmagan");
    }

    /// <summary>
    /// Ustunlar hisobi Font A va NOL belgi oralig'iga tayanadi — ikkalasi
    /// ham ochiq o'rnatiladi.
    /// </summary>
    /// <remarks>
    /// Oldingi ish printerda boshqa shrift yoki belgi oralig'i qoldirgan
    /// bo'lsa, qatorga sig'adigan belgilar soni o'zgarar va 48 belgilik
    /// qator ikkiga bo'linib, summa keyingi qatorga tushib ketardi.
    /// </remarks>
    [Fact]
    public void Shrift_va_belgi_oraligi_qulflanadi()
    {
        var bytes = EscPosReceipt.Build(Sale(), "uz", 80);

        Assert.True(Find(bytes, [0x1B, 0x4D, 0x00]) >= 0, "Font A tanlanmagan");   // ESC M 0
        Assert.True(Find(bytes, [0x1B, 0x20, 0x00]) >= 0, "belgi oralig'i nolga qo'yilmagan"); // ESC SP 0
    }

    /// <summary>
    /// Chekda QAYTARISH uchun kerak bo'ladigan raqam turadi.
    /// </summary>
    /// <remarks>
    /// <para>Qaytarish oynasi sotuvni chek raqami bo'yicha qidiradi va
    /// <c>SaleQueryService</c> uni <c>SaleNumber</c> bilan solishtiradi.
    /// Ilgari chekka sotuv identifikatorining qisqartmasi («#9BBB18»)
    /// bosilardi: u hech qanday qidiruvga tushmasdi va kassir qo'lida
    /// chek turib, sotuvni topa olmasdi.</para>
    /// </remarks>
    [Fact]
    public void Chekda_qaytarish_uchun_raqam_bor()
    {
        var lines = Lines(EscPosReceipt.Build(Sale(), "uz", 80));

        var row = Assert.Single(lines, l => l.StartsWith("Chek"));
        Assert.EndsWith("№21", row);
        // Qidiruvga tushmaydigan qisqartma qaytib kelmasin.
        Assert.DoesNotContain("9BBB18", string.Join("\n", lines));
    }

    /// <summary>
    /// Ega Sozlamalarda to'ldirgan rekvizitlar chekka CHIQADI.
    /// </summary>
    /// <remarks>
    /// Manzil, telefon va «Chek tepasidagi/pastidagi matn» maydonlari
    /// ancha vaqtdan beri bor edi, lekin chekka umuman yetib bormasdi:
    /// ega ularni to'ldirar va qog'ozda hech qachon ko'rmasdi.
    /// </remarks>
    [Fact]
    public void Dokon_rekvizitlari_chekda_chiqadi()
    {
        var data = Sale() with
        {
            MarketAddress = "Toshkent sh., Chilonzor 12",
            MarketPhone = "+998 90 123-45-67",
            ReceiptHeader = "Aksiya: 3 tadan 10% chegirma",
            ReceiptFooter = "Qaytarish 14 kun ichida",
        };

        var all = string.Join("\n", Lines(EscPosReceipt.Build(data, "uz", 80)));

        Assert.Contains("Chilonzor 12", all);
        Assert.Contains("+998 90 123-45-67", all);
        Assert.Contains("Aksiya", all);
        Assert.Contains("Qaytarish 14 kun ichida", all);
    }

    /// <summary>
    /// To'ldirilmagan maydon chekda BO'SH QATOR ham qoldirmaydi — rulon
    /// tor va har bir qator qog'oz.
    /// </summary>
    [Fact]
    public void Toldirilmagan_rekvizit_qator_egallamaydi()
    {
        var withNone = Lines(EscPosReceipt.Build(Sale(), "uz", 80));
        var withAddress = Lines(EscPosReceipt.Build(
            Sale() with { MarketAddress = "Chilonzor 12" }, "uz", 80));

        Assert.Equal(withNone.Length + 1, withAddress.Length);
    }

    /// <summary>
    /// Uzun manzil qatordan chiqib ketmaydi — printer uni o'zicha kesar
    /// va manzil yarim qolardi.
    /// </summary>
    [Fact]
    public void Uzun_manzil_qatorga_sigdiriladi()
    {
        var data = Sale() with
        {
            MarketAddress = "Toshkent shahri, Chilonzor tumani, "
                          + "Bunyodkor shoh ko'chasi, 128-uy, 2-qavat",
        };

        var lines = Lines(EscPosReceipt.Build(data, "uz", 58));

        Assert.All(lines, l => Assert.True(l.Length <= 32, $"qator {l.Length} belgidan iborat"));
        Assert.Contains(lines, l => l.Contains("Bunyodkor"));
    }

    /// <summary>Bayt ketma-ketligining o'rni; topilmasa −1.</summary>
    private static int Find(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length && hit; j++)
                hit = haystack[i + j] == needle[j];
            if (hit) return i;
        }
        return -1;
    }
}
