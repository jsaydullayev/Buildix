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

    public static byte[] Render(IReadOnlyList<LabelData> labels, double widthMm = 58, double heightMm = 40) =>
        Build(labels, widthMm, heightMm).GeneratePdf();

    /// <summary>
    /// Bitta yorliqning ko'rinishi (PNG) — chop etishdan oldin ko'rsatish uchun.
    ///
    /// <para>AYNAN shu <see cref="Build"/> hujjatidan chiqadi, ya'ni ko'rinish
    /// bilan bosiladigan narsa bir-biriga mos tushmay qolishi mumkin emas.
    /// Ko'rinishni alohida HTML/SVG bilan chizganda ikkita maket paydo bo'lardi
    /// va ular vaqt o'tib bir-biridan uzoqlashardi.</para>
    ///
    /// <para>Bu metod bazaga umuman tegmaydi: kod va nom chaqiruvchidan keladi,
    /// shuning uchun ko'rinishni ochish hech narsani o'zgartirmaydi.</para>
    /// </summary>
    public static byte[] RenderPreviewPng(LabelData label, double widthMm = 58, double heightMm = 40)
    {
        var images = Build([label with { Copies = 1 }], widthMm, heightMm)
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                // 8x — kichik yorliq ekranda tiniq ko'rinsin; 58mm da bu ~1300px.
                RasterDpi = 8 * 72,
            });
        return images.First();
    }

    private static IDocument Build(IReadOnlyList<LabelData> labels, double widthMm, double heightMm)
    {
        if (labels.Count == 0)
            throw new ArgumentException("Yorliq ro'yxati bo'sh.", nameof(labels));

        // Chiziqlar balandligi yorliqning ~42% i. Past chiziqni qo'l skaneri
        // burchak ostida o'qiy olmaydi — 30% da yorliqning pastki yarmi bo'sh
        // qolar va chiziqlar keraksiz past edi.
        var barsHeightMm = Math.Max(10, heightMm * 0.42);
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

                        // AlignMiddle — yorliq balandligi turlicha bo'lishi mumkin
                        // (58x40, 40x30, 30x20), va matn baland yorliqda tepaga
                        // yopishib, pastini bo'sh qoldirmasin.
                        page.Content().AlignMiddle().Column(col =>
                        {
                            // Nom — ikki qatorgacha, markazda. Uzun nomlar
                            // qirqiladi: yorliqda nom ma'lumot uchun, tovarni esa
                            // kod aniqlaydi.
                            col.Item().AlignCenter().Text(label.ProductName)
                                .FontSize((float)(heightMm * 0.15))
                                .SemiBold()
                                .ClampLines(2);

                            // AlignCenter TASHQARIDA: ichkarida qo'llansa u
                            // chiziqlarni o'z qutisi ichida markazlaydi, quti esa
                            // chapda qolib ketadi va kod raqamlarga nisbatan
                            // surilib ko'rinadi.
                            col.Item().PaddingTop(1, Unit.Millimetre)
                                .AlignCenter()
                                .Width((float)barsWidthMm, Unit.Millimetre)
                                .Height((float)barsHeightMm, Unit.Millimetre)
                                .Svg(BarcodeSvg.Ean13(label.Barcode, barsWidthMm, barsHeightMm));

                            // Raqamlar chiziqlar ostida — skaner ishlamay qolsa
                            // kassir kodni qo'lda kirita oladi.
                            col.Item().AlignCenter().Text(Spaced(label.Barcode))
                                .FontSize((float)(heightMm * 0.13))
                                .LetterSpacing(0.08f);

                            // Artikul kod raqamlaridan aniq ajralib tursin:
                            // ular bir-birining ostida turadi va qisqa artikul
                            // («1») kodning davomidek o'qilib ketishi mumkin.
                            // Bo'shliq + «Art.» prefiksi + kulrang rang buni
                            // uch tomonlama hal qiladi.
                            if (!string.IsNullOrWhiteSpace(label.Sku))
                                col.Item().PaddingTop((float)(heightMm * 0.04), Unit.Millimetre)
                                    .AlignCenter().Text($"Art. {label.Sku}")
                                    .FontSize((float)(heightMm * 0.1))
                                    .FontColor("#666666")
                                    .ClampLines(1);
                        });
                    });
                }
            }
        });
    }

    /// <summary>
    /// «2530305808431» → «2 530305 808431» — EAN-13 ning standart 1-6-6
    /// guruhlanishi.
    ///
    /// <para>Ilgari 3 tadan guruhlanardi va oxirida yolg'iz raqam qolardi
    /// («253 030 580 843 1»). Artikuli qisqa tovarlarda («1») bu ikkisi bir
    /// narsadek o'qilardi. 1-6-6 esa shtrix-kodning o'z bo'linishiga mos
    /// keladi — chiziqlar ham aynan shunday guruhlangan.</para>
    /// </summary>
    private static string Spaced(string code) =>
        code.Length == 13
            ? $"{code[0]} {code[1..7]} {code[7..]}"
            : code;
}
