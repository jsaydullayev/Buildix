using Buildix.Application.Services.Reports;

namespace Buildix.Tests;

/// <summary>
/// Kassa chekining o'lchami.
///
/// <para>Bu sinovlar haqiqiy nosozlikdan keyin yozildi: chek printerdan
/// yarim metr uzunlikda, har bir harfi alohida qatorda chiqdi. Sabab
/// chizuvchida emas — chop etish yo'lida edi (brauzer oynasi A4 va
/// «sahifaga moslash» qo'llardi). Lekin o'sha tekshiruvdan keyin chek eni
/// endi sozlamadan keladi va u chizuvchiga haqiqatan yetib borishini
/// biror narsa kafolatlashi kerak.</para>
/// </summary>
public class ReceiptRenderTests
{
    private static ReportPdfRenderer.InvoiceData Sale() => new(
        MarketName: "Taxtapul stroy",
        MarketDescription: "Qurilish mollari",
        SellerName: "Jaxongir",
        CustomerName: "Xoshim",
        InvoiceNumber: Guid.NewGuid(),
        Date: new DateTime(2026, 8, 28, 1, 6, 0, DateTimeKind.Utc),
        PaymentType: "Naqd",
        Items:
        [
            new ReportPdfRenderer.InvoiceItemData("DSP", 1, 380_000, 380_000, null, false),
            new ReportPdfRenderer.InvoiceItemData("sement", 1, 1_000, 1_000, null, false),
            new ReportPdfRenderer.InvoiceItemData("taxta", 1, 70_000, 70_000, null, false),
        ],
        TotalAmount: 451_000,
        PaidAmount: 451_000,
        RemainingAmount: 0,
        Status: "Paid",
        SubtotalAmount: 451_000,
        DiscountAmount: 0);

    /// <summary>PNG sarlavhasidan (IHDR) o'lchamni oladi.</summary>
    private static (int Width, int Height) PngSize(byte[] png)
    {
        // PNG: 8 bayt imzo, keyin IHDR uzunligi (4) va turi (4) — en 16-baytdan.
        Assert.True(png.Length > 24, "PNG juda kichik");
        int Be(int at) => (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
        return (Be(16), Be(20));
    }

    /// <summary>
    /// So'ralgan en RASMGA yetib boradi.
    ///
    /// <para>58 mm rulon 80 mm dan tor bo'lishi shart. Ilgari en interfeysga
    /// qattiq 80 deb yozilgan edi va bu farq umuman yuzaga kelmasdi: 58 mm
    /// printerli do'kon 80 mm chek olardi, drayver esa uni o'zicha siqib
    /// bosardi.</para>
    /// </summary>
    [Fact]
    public void Rulon_eni_rasmga_yetib_boradi()
    {
        var (wide, _) = PngSize(ReportPdfRenderer.RenderThermalReceiptPng(Sale(), "uz", 80));
        var (narrow, _) = PngSize(ReportPdfRenderer.RenderThermalReceiptPng(Sale(), "uz", 58));

        Assert.True(narrow < wide, $"58 mm ({narrow}px) 80 mm ({wide}px) dan tor bo'lishi kerak");

        // Nisbat rulon enlariga mos: 58/80 = 0.725.
        var ratio = (double)narrow / wide;
        Assert.True(Math.Abs(ratio - 58.0 / 80.0) < 0.02, $"nisbat {ratio:F3}, kutilgani 0.725");
    }

    /// <summary>
    /// Chek TIK bo'ladi — eni balandligidan kichik.
    ///
    /// <para>Qog'ozdan chiqqan chek yotiq va har bir harfi alohida qatorda
    /// edi. Chizuvchining o'zi bunday qilmasligini shu yerda qat'iy
    /// belgilaymiz.</para>
    /// </summary>
    [Fact]
    public void Chek_tik_chiqadi()
    {
        var (w, h) = PngSize(ReportPdfRenderer.RenderThermalReceiptPng(Sale(), "uz", 80));

        Assert.True(h > w, $"chek tik bo'lishi kerak: {w}x{h}");
    }

    /// <summary>
    /// Standart bo'lmagan en berilsa ham chek chiqadi: chizuvchi ikki
    /// qiymatdan biriga tushadi va hech qachon yiqilmaydi.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    public void Yaroqsiz_en_chekni_yiqitmaydi(int widthMm)
    {
        var png = ReportPdfRenderer.RenderThermalReceiptPng(Sale(), "uz", widthMm);

        var (w, h) = PngSize(png);
        Assert.True(w > 0 && h > 0);
    }

    /// <summary>Rasm va PDF AYNAN bir hujjatdan — ikkisi ham chiqadi.</summary>
    [Fact]
    public void Rasm_va_pdf_birga_chiqadi()
    {
        var pdf = ReportPdfRenderer.RenderThermalReceiptPdf(Sale(), "uz", 80);
        var png = ReportPdfRenderer.RenderThermalReceiptPng(Sale(), "uz", 80);

        Assert.True(pdf.Length > 1000, "PDF bo'sh");
        Assert.True(png.Length > 1000, "PNG bo'sh");
    }
}
