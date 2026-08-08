using Buildix.Application.Services.Barcodes;
using ZXing.OneD;

namespace Buildix.Tests;

/// <summary>
/// Kod mashina o'qiydigan narsa: xatosi ekranda ko'rinmaydi, faqat skaner
/// tovarni tanimay qo'ygandagina bilinadi — u ham printer sotib olinib,
/// yuzlab yorliq bosilgandan keyin. Shuning uchun kodlash ikki tomonlama
/// tekshiriladi: ma'lum kodlarga qarshi va ZXing bilan teskari o'qib.
/// </summary>
public class Ean13Tests
{
    // Sanoatda keng ishlatiladigan namunalar — nazorat raqami ma'lum.
    [Theory]
    [InlineData("400638133393", 1)]
    [InlineData("590123412345", 7)]
    [InlineData("978020137962", 4)]
    public void Check_digit_matches_known_codes(string first12, int expected)
    {
        Assert.Equal(expected, Ean13.CheckDigit(first12));
    }

    [Fact]
    public void Validation_rejects_a_broken_check_digit()
    {
        Assert.True(Ean13.IsValid("4006381333931"));
        // Oxirgi raqam bitta o'zgartirildi — skaner ham aynan shuni rad etadi.
        Assert.False(Ean13.IsValid("4006381333932"));
        Assert.False(Ean13.IsValid("40063813339"));    // qisqa
        Assert.False(Ean13.IsValid("400638133393X")); // raqam emas
        Assert.False(Ean13.IsValid(null));
    }

    [Fact]
    public void Generated_codes_are_valid_and_internal()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = Ean13.NewInternal();
            Assert.True(Ean13.IsValid(code), $"'{code}' nazorat raqami noto'g'ri");
            Assert.True(Ean13.IsInternal(code), $"'{code}' ichki diapazonda emas");
            // 20…29 — GS1 hech kimga bermaydigan diapazon, ya'ni haqiqiy zavod
            // kodi bilan hech qachon to'qnashmaydi.
            var prefix = int.Parse(code[..2]);
            Assert.InRange(prefix, 20, 29);
        }
    }

    [Fact]
    public void Generated_codes_do_not_repeat_in_a_large_batch()
    {
        // Yagonalikni baza indeksi kafolatlaydi, lekin generator o'zi ham
        // takrorlab tashlamasligi kerak — aks holda har saqlashda qayta urinish
        // kerak bo'lib, tizim sekinlashardi.
        var codes = Enumerable.Range(0, 5_000).Select(_ => Ean13.NewInternal()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void ZXing_accepts_every_generated_code()
    {
        // Mustaqil tasdiq: ZXing kodlashdan oldin nazorat raqamini o'zi
        // tekshiradi. Bizning hisobimiz noto'g'ri bo'lsa, bu yerda yiqiladi.
        var writer = new EAN13Writer();
        for (var i = 0; i < 50; i++)
        {
            var code = Ean13.NewInternal();
            var matrix = writer.encode(code, ZXing.BarcodeFormat.EAN_13, 0, 1);
            // 113 = 95 ta chiziq moduli + har ikki chetda 9 moduldan bo'sh zona.
            // Bo'sh zona standart talabi va uni tashlab yuborish mumkin emas:
            // usiz skanerlar kodning boshi qayerdaligini aniqlay olmaydi va
            // ko'pincha umuman o'qimaydi. SVG ni ham shu kenglikda chizamiz.
            Assert.Equal(113, matrix.Width);
        }
    }

    [Fact]
    public void Svg_contains_bars_and_scales_to_the_requested_size()
    {
        var svg = BarcodeSvg.Ean13("4006381333931", widthMm: 40, heightMm: 12);

        // Bo'sh zona bilan birga — u yorliqda ham saqlanishi shart.
        Assert.Contains("viewBox=\"0 0 113 100\"", svg);
        Assert.Contains("width=\"40mm\"", svg);
        Assert.Contains("height=\"12mm\"", svg);
        Assert.Contains("<rect", svg);
        // Chiziqlar birlashtirilgani uchun 95 tadan ancha kam bo'lishi kerak.
        var rects = svg.Split("<rect").Length - 1;
        Assert.InRange(rects, 20, 60);
    }

    [Fact]
    public void Svg_reproduces_the_encoder_pattern_module_for_module()
    {
        // Eng muhim tekshiruv: chizilgan narsa ZXing kodlagan naqshning AYNAN
        // o'zimi. Bitta modul surilsa yoki tushib qolsa, yorliq bosiladi-yu
        // skaner uni o'qimaydi — buni faqat kassada, printer olingandan keyin
        // bilib qolishardi. Rasterlashtirmasdan, to'g'ridan-to'g'ri
        // solishtiramiz: SVG dagi to'rtburchaklardan modul massivini tiklaymiz.
        const string code = "4006381333931";
        var expected = new EAN13Writer().encode(code, ZXing.BarcodeFormat.EAN_13, 0, 1);

        var svg = BarcodeSvg.Ean13(code, 40, 12);
        var actual = new bool[expected.Width];
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(svg, @"<rect x=""(\d+)"" y=""0"" width=""(\d+)"""))
        {
            var start = int.Parse(m.Groups[1].Value);
            var width = int.Parse(m.Groups[2].Value);
            for (var i = start; i < start + width; i++) actual[i] = true;
        }

        for (var x = 0; x < expected.Width; x++)
            Assert.True(expected[x, 0] == actual[x], $"{x}-modul mos kelmadi");
    }

    [Fact]
    public void Svg_refuses_an_invalid_code()
    {
        // Yaroqsiz kod jimgina bo'sh yorliq berib qo'ymasin.
        Assert.Throws<ArgumentException>(() => BarcodeSvg.Ean13("1234567890123", 40, 12));
    }
}
