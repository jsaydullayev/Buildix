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
    public void An_invalid_code_is_refused_rather_than_printed_blank()
    {
        // Yaroqsiz kod bilan yorliq bosilsa, u skanerlanmaydi va buni faqat
        // kassada bilishadi — shuning uchun bu yerda to'xtaymiz.
        Assert.Throws<ArgumentException>(() =>
            LabelPdfRenderer.Render([new LabelData("Sement", "1234567890123", "CEM")]));
    }
}
