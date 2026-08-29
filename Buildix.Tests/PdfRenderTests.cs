using Buildix.Application.DTOs;
using Buildix.Application.Services.Reports;

namespace Buildix.Tests;

/// <summary>
/// Layout smoke tests for the branded PDFs. The renderers draw the Buildix mark
/// through QuestPDF's SVG element, which only fails at render time (never at
/// compile time) — so every document is actually generated here to catch a
/// malformed mark or a broken layout before it reaches a customer's invoice.
///
/// Set BUILDIX_PDF_DUMP to a directory to also write the generated files there
/// and eyeball the branding.
/// </summary>
public class PdfRenderTests
{
    private static void AssertIsPdf(byte[] bytes, string name)
    {
        Assert.True(bytes.Length > 1000, $"{name}: suspiciously small PDF ({bytes.Length} bytes)");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);

        var dumpDir = Environment.GetEnvironmentVariable("BUILDIX_PDF_DUMP");
        if (!string.IsNullOrWhiteSpace(dumpDir))
        {
            Directory.CreateDirectory(dumpDir);
            File.WriteAllBytes(Path.Combine(dumpDir, $"{name}.pdf"), bytes);
        }
    }

    private static ReportPdfRenderer.InvoiceData SampleInvoice() => new(
        MarketName: "Qurilish Baza",
        MarketDescription: "Chilonzor, 12-kvartal",
        SellerName: "Aziz Karimov",
        CustomerName: "Sardor Umarov",
        InvoiceNumber: Guid.Parse("8f14e45f-ea2b-4c1f-9d3a-77b0c1e2d3f4"),
        SaleNumber: 137,
        Date: new DateTime(2026, 7, 25, 14, 30, 0),
        PaymentType: "Naqd",
        Items:
        [
            new("Sement M400 (50kg)", 12m, 62_000m, 744_000m, null, false),
            new("G'isht qizil", 500m, 1_400m, 700_000m, "Palletda", false),
            new("Armatura 12mm", 30m, 48_000m, 1_440_000m, null, true),
        ],
        TotalAmount: 2_784_000m,
        PaidAmount: 2_000_000m,
        RemainingAmount: 784_000m,
        Status: "Debt",
        SubtotalAmount: 2_884_000m,
        DiscountAmount: 100_000m);

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void Invoice_renders(string lang)
        => AssertIsPdf(ReportPdfRenderer.RenderInvoicePdf(SampleInvoice(), lang), $"invoice-{lang}");

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void InvoiceCompact_renders(string lang)
        => AssertIsPdf(ReportPdfRenderer.RenderInvoiceCompactPdf(SampleInvoice(), lang), $"invoice-compact-{lang}");

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void SalesList_renders(string lang)
    {
        ReportPdfRenderer.SalesReportItem[] items =
        [
            new(1, new DateTime(2026, 7, 25, 9, 15, 0), "Sardor Umarov", "Aziz Karimov",
                "Sement M400 (50kg)", 12m, 55_000m, 62_000m, 744_000m, 84_000m, "Paid"),
            new(2, new DateTime(2026, 7, 25, 11, 40, 0), "Nodira Yusupova", "Aziz Karimov",
                "G'isht qizil", 500m, 1_150m, 1_400m, 700_000m, 125_000m, "Debt"),
        ];

        var bytes = ReportPdfRenderer.RenderSalesListPdf(
            items, new DateTime(2026, 7, 1), new DateTime(2026, 7, 25),
            includeProfit: true, includeCost: true,
            totalSales: 1_444_000m, totalProfit: 209_000m, receiptCount: 2,
            generatedAtLocal: new DateTime(2026, 7, 25, 18, 0, 0), lang: lang);

        AssertIsPdf(bytes, $"sales-list-{lang}");
    }

    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    public void SummaryReport_renders(string lang)
    {
        (string, string, string)[] kpis =
        [
            ("Jami savdo", "1 444 000 so'm", "#0F172A"),
            ("Sof foyda", "209 000 so'm", "#16A34A"),
            ("Cheklar", "2", "#0F172A"),
        ];
        PaymentBreakdownDto[] payments =
        [
            new("Cash", 744_000m, 1),
            new("Transfer", 700_000m, 1),
        ];

        var bytes = ReportPdfRenderer.RenderSummaryReportPdf(
            "KUNLIK HISOBOT", "25.07.2026", kpis, payments,
            new DateTime(2026, 7, 25, 18, 0, 0), lang);

        AssertIsPdf(bytes, $"summary-{lang}");
    }
}
