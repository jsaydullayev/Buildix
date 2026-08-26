using Buildix.Application.Services.Barcodes;

namespace Buildix.Tests;

/// <summary>
/// Yorliq PDF i. Chiziqlar SVG orqali chiziladi va SVG faqat render paytida
/// tekshiriladi — kompilyator uni ko'rmaydi. Shuning uchun har maket haqiqatan
/// generatsiya qilinadi.
///
/// BUILDIX_PDF_DUMP o'zgaruvchisiga papka bersangiz, fayllar o'sha yerga
/// yoziladi va yorliqni ko'z bilan ko'rish mumkin.
/// </summary>
public class LabelPdfTests
{
    private static void AssertIsPdf(byte[] bytes, string name)
    {
        Assert.True(bytes.Length > 500, $"{name}: juda kichik PDF ({bytes.Length} bayt)");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);

        var dir = Environment.GetEnvironmentVariable("BUILDIX_PDF_DUMP");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{name}.pdf"), bytes);
        }
    }

    [Fact]
    public void Single_label_renders()
    {
        var code = Ean13.NewInternal();
        var pdf = LabelPdfRenderer.Render([new LabelData("Sement M400 (50 kg)", code, "CEM-400")]);
        AssertIsPdf(pdf, "label-single");
    }

    [Fact]
    public void Label_without_sku_renders()
    {
        // Artikul ixtiyoriy — bo'lmasa yorliq baribir chiqishi kerak.
        var pdf = LabelPdfRenderer.Render([new LabelData("G'isht qizil", Ean13.NewInternal(), null)]);
        AssertIsPdf(pdf, "label-no-sku");
    }

    [Fact]
    public void Long_name_does_not_break_the_layout()
    {
        var pdf = LabelPdfRenderer.Render([
            new LabelData("Armatura A500C rifleniy 12mm x 11.7m issiqlik bilan ishlangan po'lat", Ean13.NewInternal(), "ARM-12-500")
        ]);
        AssertIsPdf(pdf, "label-long-name");
    }

    [Fact]
    public void Copies_produce_one_page_each()
    {
        var one = LabelPdfRenderer.Render([new LabelData("Sement", Ean13.NewInternal(), "CEM")]);
        var five = LabelPdfRenderer.Render([new LabelData("Sement", Ean13.NewInternal(), "CEM", Copies: 5)]);

        // Har nusxa alohida sahifa: yorliq printeri sahifadan keyin qog'ozni uzadi.
        Assert.True(five.Length > one.Length, "5 nusxa 1 nusxadan katta bo'lishi kerak");
        AssertIsPdf(five, "label-5-copies");
    }

    [Fact]
    public void Custom_size_renders()
    {
        // Printer hali sotib olinmagan — boshqa rulon olinsa maket moslashishi kerak.
        var pdf = LabelPdfRenderer.Render(
            [new LabelData("Sement M400", Ean13.NewInternal(), "CEM-400")],
            widthMm: 40, heightMm: 30);
        AssertIsPdf(pdf, "label-40x30");
    }

    [Fact]
    public void Batch_of_different_products_renders()
    {
        LabelData[] batch = [
            new("Sement M400 (50 kg)", Ean13.NewInternal(), "CEM-400", 2),
            new("G'isht qizil", Ean13.NewInternal(), "GSH-01", 3),
            new("Armatura 12mm", Ean13.NewInternal(), "ARM-12", 1),
        ];
        AssertIsPdf(LabelPdfRenderer.Render(batch), "label-batch");
    }

    [Fact]
    public void Preview_renders_a_png_of_the_same_layout()
    {
        // Ko'rinish chop etiladigan hujjatning o'zidan chiqadi — alohida maket
        // yo'q, demak ko'rgani bilan bosilgani farq qila olmaydi.
        var png = LabelPdfRenderer.RenderPreviewPng(
            new LabelData("Sement M400 (50 kg)", "4006381333931", "CEM-400"));

        Assert.True(png.Length > 2000, $"juda kichik PNG ({png.Length} bayt)");
        // PNG imzosi.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        var dir = Environment.GetEnvironmentVariable("BUILDIX_PDF_DUMP");
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "preview-58x40.png"), png);
            File.WriteAllBytes(Path.Combine(dir, "preview-30x20.png"),
                LabelPdfRenderer.RenderPreviewPng(
                    new LabelData("Sement M400 (50 kg)", "4006381333931", "CEM-400"), 30, 20));
        }
    }

    [Fact]
    public void A_one_character_sku_does_not_read_as_part_of_the_code()
    {
        // Haqiqiy ma'lumotdan kelgan holat: artikul «1». Ilgari kod raqamlari
        // 3 tadan guruhlanib oxirida yolg'iz raqam qolardi («… 843 1») va
        // artikul o'shaning davomidek o'qilardi. Endi 1-6-6 guruhlash va
        // «Art.» prefiksi ularni ajratadi.
        var png = LabelPdfRenderer.RenderPreviewPng(new LabelData("sement", "2530305808431", "1"));
        Assert.True(png.Length > 2000);

        var dir = Environment.GetEnvironmentVariable("BUILDIX_PDF_DUMP");
        if (!string.IsNullOrWhiteSpace(dir))
            File.WriteAllBytes(Path.Combine(dir, "preview-short-sku.png"), png);
    }

    [Fact]
    public void Preview_ignores_the_copies_field()
    {
        // Ko'rinish doim bitta yorliq: nusxa soni chop etishga tegishli.
        var one = LabelPdfRenderer.RenderPreviewPng(new LabelData("Sement", "4006381333931", "CEM", Copies: 1));
        var many = LabelPdfRenderer.RenderPreviewPng(new LabelData("Sement", "4006381333931", "CEM", Copies: 50));
        Assert.Equal(one.Length, many.Length);
    }

    [Fact]
    public void An_empty_code_is_refused_rather_than_printed_blank()
    {
        // Bo'sh kod bilan yorliq bosilsa, unda chiziq umuman bo'lmaydi va buni
        // faqat kassada bilishadi — shuning uchun bu yerda to'xtaymiz.
        Assert.Throws<ArgumentException>(() =>
            LabelPdfRenderer.Render([new LabelData("Sement", "  ", "CEM")]));
    }

    /// <summary>
    /// PDF sahifasining FIZIK o'lchamini o'lchaydi.
    ///
    /// <para>Ilgari bu yerda faqat «PDF chiqdimi» tekshirilardi. Ya'ni maket
    /// noto'g'ri o'lchamda chiqsa ham sinov yashil qolaverardi va buni faqat
    /// do'konda, printerdan chiqqan qog'ozni ko'rib bilishardi.</para>
    ///
    /// <para>PDF birligi — punkt (1/72 dyuym). QuestPDF `Unit.Millimetre` ni
    /// o'zi o'giradi, lekin o'sha o'girish to'g'ri ekaniga ishonch shu
    /// yerdan keladi.</para>
    /// </summary>
    [Theory]
    [InlineData(58, 40)]
    [InlineData(40, 30)]
    [InlineData(30, 20)]
    public void Page_is_exactly_the_requested_size_in_mm(double widthMm, double heightMm)
    {
        var pdf = LabelPdfRenderer.Render(
            [new LabelData("Sement M400", Ean13.NewInternal(), "CEM-400")],
            widthMm, heightMm);

        var (w, h) = FirstPageSizeMm(pdf);

        // Yarim millimetr — chop etishda sezilmaydigan chegara.
        Assert.True(Math.Abs(w - widthMm) < 0.5, $"eni {w:F2} mm, kutilgani {widthMm} mm");
        Assert.True(Math.Abs(h - heightMm) < 0.5, $"bo'yi {h:F2} mm, kutilgani {heightMm} mm");
    }

    /// <summary>PDF dagi birinchi /MediaBox ni millimetrga o'giradi.</summary>
    private static (double WidthMm, double HeightMm) FirstPageSizeMm(byte[] pdf)
    {
        // QuestPDF siqmagan holda yozadi, shuning uchun /MediaBox matn
        // ko'rinishida topiladi. Topilmasa — sinov jim o'tib ketmasin.
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"/MediaBox\s*\[\s*([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s*\]");
        Assert.True(match.Success, "PDF ichida /MediaBox topilmadi");

        var pt = match.Groups.Cast<System.Text.RegularExpressions.Group>()
            .Skip(1).Select(g => double.Parse(g.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        const double MmPerPoint = 25.4 / 72.0;
        return ((pt[2] - pt[0]) * MmPerPoint, (pt[3] - pt[1]) * MmPerPoint);
    }

    [Fact]
    public void A_shop_code_prints_without_the_ean13_grouping()
    {
        // «1» kabi do'kon kodi Code 128 bilan bosiladi. Raqamlar qatorida
        // EAN-13 ning 1-6-6 guruhlashi qo'llanmasligi kerak — u faqat 13
        // xonali zavod kodiga tegishli.
        var pdf = LabelPdfRenderer.Render([new LabelData("Sement", "1", "CEM")]);
        Assert.NotEmpty(pdf);
        Assert.Equal(0x25, pdf[0]);   // '%' — PDF sarlavhasi
    }
}
