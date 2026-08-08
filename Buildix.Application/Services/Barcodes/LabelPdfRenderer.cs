using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Buildix.Application.Services.Barcodes;

/// <summary>Bitta yorliqqa chiqadigan ma'lumot.</summary>
public sealed record LabelData(string ProductName, string Barcode, string? Sku, int Copies = 1);

/// <summary>
/// Tovar yorliqlari — termal yorliq printeri uchun.
///
/// <para><b>Bir yorliq = bir sahifa.</b> Yorliq printeri rulondan bosadi va har
/// sahifadan keyin qog'ozni kesadi/uzadi. Nusxa kerak bo'lsa sahifa
/// takrorlanadi — bitta sahifaga ikkita yorliq joylash printerni chalkashtiradi.</para>
///
/// <para><b>O'lcham sozlanadi.</b> Standart 58×40 mm — arzon modellarda
/// (Xprinter, TSC) eng keng tarqalgani. Boshqa rulon olinsa, chaqiruvchi
/// o'lchamni beradi va maket unga moslashadi.</para>
/// </summary>
public static class LabelPdfRenderer
{
    static LabelPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(IReadOnlyList<LabelData> labels, double widthMm = 58, double heightMm = 40)
    {
        if (labels.Count == 0)
            throw new ArgumentException("Yorliq ro'yxati bo'sh.", nameof(labels));

        // Chiziqlar balandligi yorliqning ~30% i: pastroq bo'lsa qo'l skaneri
        // burchak ostida o'qiy olmaydi, balandroq bo'lsa nomga joy qolmaydi.
        var barsHeightMm = Math.Max(8, heightMm * 0.3);
        var barsWidthMm = widthMm - 6; // ikki chetda 3 mm — printer chekkasi ishonchsiz

        return Document.Create(container =>
        {
            foreach (var label in labels)
            {
                // Nusxa soni = sahifa soni.
                for (var copy = 0; copy < Math.Max(1, label.Copies); copy++)
                {
                    container.Page(page =>
                    {
                        page.Size((float)widthMm, (float)heightMm, Unit.Millimetre);
                        page.Margin(2, Unit.Millimetre);
                        page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontColor("#000000"));

                        page.Content().Column(col =>
                        {
                            // Nom — ikki qatorgacha. Uzun nomlar qirqiladi: yorliqda
                            // nom ma'lumot uchun, tovarni esa kod aniqlaydi.
                            col.Item().Text(label.ProductName)
                                .FontSize((float)(heightMm * 0.16))
                                .SemiBold()
                                .ClampLines(2);

                            col.Item().PaddingTop(1, Unit.Millimetre)
                                .Height((float)barsHeightMm, Unit.Millimetre)
                                .Width((float)barsWidthMm, Unit.Millimetre)
                                .AlignCenter()
                                .Svg(BarcodeSvg.Ean13(label.Barcode, barsWidthMm, barsHeightMm));

                            // Raqamlar chiziqlar ostida — skaner ishlamay qolsa
                            // kassir kodni qo'lda kirita oladi.
                            col.Item().AlignCenter().Text(Spaced(label.Barcode))
                                .FontSize((float)(heightMm * 0.13))
                                .LetterSpacing(0.08f);

                            if (!string.IsNullOrWhiteSpace(label.Sku))
                                col.Item().AlignCenter().Text(label.Sku)
                                    .FontSize((float)(heightMm * 0.11))
                                    .FontColor("#333333")
                                    .ClampLines(1);
                        });
                    });
                }
            }
        }).GeneratePdf();
    }

    /// <summary>«2012345678903» → «201 234 567 890 3» — qo'lda kiritish oson bo'lsin.</summary>
    private static string Spaced(string code) =>
        string.Join(' ', Enumerable.Range(0, (code.Length + 2) / 3)
            .Select(i => code.Substring(i * 3, Math.Min(3, code.Length - i * 3))));
}
