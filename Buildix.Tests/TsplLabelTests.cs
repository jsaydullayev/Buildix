using System.Globalization;
using System.Text;
using Buildix.Application.DTOs;
using Buildix.Application.Services.Barcodes;
using Buildix.Application.Services.Printing;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Yorliqning TSPL matni — printer AYNAN shu buyruqlarni oladi.
/// </summary>
/// <remarks>
/// <para>Sinovlar baytlarni matnga o'girib, buyruqlar bo'yicha tekshiradi
/// (<c>EscPosReceiptTests</c> dagi bilan bir xil yondashuv). Yorliqning
/// ko'rinishini sinovdan o'tkazib bo'lmaydi — u qog'ozda paydo bo'ladi —
/// lekin printerga BORADIGAN buyruqni to'liq tekshirish mumkin, va aynan
/// shu yerda qimmat xatolar bo'lgan.</para>
/// </remarks>
public class TsplLabelTests
{
    private static LabelData Cement(int copies = 1) =>
        new("Sement M400", "2530305808431", "A-125", copies);

    /// <summary>Baytlarni o'qiladigan matnga o'giradi.</summary>
    private static string Text(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(866).GetString(bytes);
    }

    private static string Build(
        LabelData label, double w = 57, double h = 38, double gap = 2, double offset = 0) =>
        Text(TsplLabel.Build([label], w, h, gap, offset));

    // ── O'lcham ─────────────────────────────────────────────────────────

    /// <summary>
    /// Butun o'zgarishning maqsadi: o'lcham JOBNING ICHIDA ketadi, ya'ni
    /// drayverdagi qog'oz o'lchamiga bog'liq emas.
    /// </summary>
    [Fact]
    public void Olcham_jobning_ichida_beriladi()
    {
        var tspl = Build(Cement(), w: 57, h: 38);

        Assert.Contains("SIZE 57 mm,38 mm", tspl);
        Assert.Contains("GAP 2 mm,0 mm", tspl);
    }

    /// <summary>
    /// O'nlik ajratgich — HAR DOIM nuqta. Do'kon Windows'i rus tilida va u
    /// yerda sukut bo'yicha vergul qo'yiladi; «SIZE 57,5 mm» buyrug'ini
    /// printer tushunmaydi va uni jimgina tashlab yuboradi — yorliq esa
    /// oldingi o'lchamda chiqaveradi.
    /// </summary>
    [Fact]
    public void Onlik_ajratgich_har_doim_nuqta()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var tspl = Build(Cement(), w: 57.5, h: 38.5, gap: 2.5);

            Assert.Contains("SIZE 57.5 mm,38.5 mm", tspl);
            Assert.Contains("GAP 2.5 mm", tspl);
            Assert.DoesNotContain("57,5", tspl);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // ── Siljish ─────────────────────────────────────────────────────────

    /// <summary>
    /// Musbat siljish nuqtada beriladi: 2 mm × 8 nuqta/mm = 16.
    /// </summary>
    [Fact]
    public void Musbat_siljish_nuqtaga_ogiriladi()
    {
        Assert.Contains("SHIFT 16", Build(Cement(), offset: 2));
    }

    /// <summary>
    /// Manfiy siljish HECH QACHON yuborilmaydi — nolga siqiladi.
    /// </summary>
    /// <remarks>
    /// TSPL da <c>SHIFT</c> diapazoni 0…1016. Manfiy son yuborilganda
    /// firmware uni ishorasiz deb o'qib, maketni bir necha santimetrga
    /// sakratib yuboradi — haqiqiy printerda maket bitta yorliq o'rniga
    /// IKKITASIGA bo'linib tushdi.
    /// </remarks>
    [Theory]
    [InlineData(-2)]
    [InlineData(-100)]
    public void Manfiy_siljish_nolga_siqiladi(double offset)
    {
        var tspl = Build(Cement(), offset: offset);

        Assert.Contains("SHIFT 0", tspl);
        Assert.DoesNotContain("SHIFT -", tspl);
    }

    /// <summary>
    /// Siljish NOL bo'lganda ham buyruq yuboriladi.
    /// </summary>
    /// <remarks>
    /// <para><c>SHIFT</c> — printerda SAQLANADIGAN sozlama: u keyingi
    /// buyruqqacha yoki quvvat o'chgunicha turadi. Yubormaslik «siljish
    /// yo'q» degani emas, «oldingi jobdan qolganini saqlab qol» degani.</para>
    ///
    /// <para>Ilgari nol bo'lganda buyruq tashlab ketilardi va 57×38 dan
    /// 57×30 ga o'tib siljishni nolga qo'ygan do'konda yorliqlar baribir
    /// surilgan chiqaverardi — buni interfeysdan tuzatib bo'lmasdi, chunki
    /// manfiy qiymat taqiqlangan va nol hech narsa qilmasdi.</para>
    /// </remarks>
    [Fact]
    public void Nol_siljish_ham_yuboriladi()
    {
        Assert.Contains("SHIFT 0", Build(Cement(), offset: 0));
    }

    /// <summary>
    /// Kalibrovka va sinov yorlig'i ham siljishni ochiq belgilaydi —
    /// ular NOSOZLIKNI aniqlash uchun bosiladi va oldingi jobdan qolgan
    /// siljishni ko'rsatib qo'ysa, texnik mavjud bo'lmagan muammoni
    /// tuzatib o'tirardi.
    /// </summary>
    [Fact]
    public void Kalibrovka_va_sinov_siljishni_ochiq_belgilaydi()
    {
        Assert.Contains("SHIFT 0", Text(TsplLabel.Calibrate(57, 38)));
        Assert.Contains("SHIFT 0", Text(TsplLabel.TestLabel(57, 38)));
        Assert.Contains("SHIFT 16", Text(TsplLabel.TestLabel(57, 38, 2, 2)));
    }

    // ── Nusxa ───────────────────────────────────────────────────────────

    /// <summary>
    /// Nusxa printerning O'Z sanog'i bilan: besh yorliq uchun bitta
    /// <c>PRINT 1,5</c>. Rasm yo'lida buning uchun besh marta sahifa
    /// takrorlanar va besh barobar ma'lumot uzatilardi.
    /// </summary>
    [Fact]
    public void Nusxa_bitta_PRINT_buyrugi_bilan()
    {
        var tspl = Build(Cement(copies: 5));

        Assert.Contains("PRINT 1,5", tspl);
        Assert.Equal(1, tspl.Split("PRINT ").Length - 1);
    }

    /// <summary>Nusxa berilmasa ham kamida bitta chiqadi.</summary>
    [Fact]
    public void Nusxa_kamida_bitta()
    {
        Assert.Contains("PRINT 1,1", Build(new LabelData("X", "2530305808431", null, 0)));
    }

    /// <summary>Har bir tovar o'z CLS va PRINT i bilan ketadi.</summary>
    [Fact]
    public void Har_tovar_alohida_yorliq()
    {
        var tspl = Text(TsplLabel.Build(
            [Cement(), new LabelData("Taxta", "2530305808431", null, 2)], 57, 38));

        Assert.Equal(2, tspl.Split("CLS").Length - 1);
        Assert.Contains("PRINT 1,1", tspl);
        Assert.Contains("PRINT 1,2", tspl);
    }

    // ── Shtrix kod ──────────────────────────────────────────────────────

    /// <summary>13 xonali to'g'ri kod — EAN-13 sifatida.</summary>
    [Fact]
    public void Ean13_kod_turi_tanlanadi()
    {
        Assert.Contains("\"EAN13\"", Build(Cement()));
    }

    /// <summary>
    /// EAN-13 bo'lmagan kod — Code 128. Omborchi kiritgan «1» aynan «1»
    /// bo'lib skanerlanishi kerak.
    /// </summary>
    [Fact]
    public void Ean13_bolmasa_Code128()
    {
        var tspl = Build(new LabelData("Qum", "1", null));

        Assert.Contains("\"128\"", tspl);
        Assert.DoesNotContain("\"EAN13\"", tspl);
    }

    /// <summary>
    /// Kod yorliqdan chiqib ketmaydi: chap chekkasi ham, o'ng chekkasi ham
    /// yorliq ichida qoladi.
    /// </summary>
    [Theory]
    [InlineData(57, 38)]
    [InlineData(58, 40)]
    [InlineData(40, 30)]
    [InlineData(30, 20)]
    public void Kod_yorliq_ichida_qoladi(double w, double h)
    {
        var tspl = Build(Cement(), w, h);
        var widthDots = (int)Math.Round(w * 8);

        // BARCODE x,y,"type",height,hri,rot,narrow,wide,"code"
        var line = tspl.Split("\r\n").Single(l => l.StartsWith("BARCODE ", StringComparison.Ordinal));
        var parts = line["BARCODE ".Length..].Split(',');
        var x = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var narrow = int.Parse(parts[6], CultureInfo.InvariantCulture);

        // ZXing ning moduli jim zonani ALLAQACHON o'z ichiga oladi
        // (EAN-13 = 95 + 2×9). Ustiga qo'shilsa kod chapga suriladi.
        var modules = BarcodeSvg.ModuleCount("2530305808431");
        Assert.True(x >= 0, $"chap chekka manfiy: {x}");
        Assert.True(x + modules * narrow <= widthDots,
            $"kod yorliqdan chiqdi: {x} + {modules}×{narrow} > {widthDots}");

        // Va u MARKAZDA: chap va o'ng chekka farqi bir nuqtadan oshmasin
        // (butun songa bo'lish sababli bitta nuqta farq bo'lishi mumkin).
        var right = widthDots - (x + modules * narrow);
        Assert.True(Math.Abs(x - right) <= 1, $"kod markazda emas: chap {x}, o'ng {right}");
    }

    // ── Matn ────────────────────────────────────────────────────────────

    /// <summary>
    /// TSPL ning <c>TEXT</c> buyrug'i satrni O'ZI ko'chirmaydi — uzun nom
    /// yorliqdan chiqib ketardi. Shuning uchun u ikki qatorga bo'linadi.
    /// </summary>
    [Fact]
    public void Uzun_nom_ikki_qatorga_bolinadi()
    {
        var tspl = Build(new LabelData(
            "Profnastil devor uchun oq rangli 0.45 mm qalinlikda", "2530305808431", null));

        Assert.Equal(2, tspl.Split("\r\n").Count(l => l.StartsWith("TEXT ", StringComparison.Ordinal)
            && !l.Contains("A-125", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Tipografik apostrof CP866 da yo'q — printer uning o'rniga tasodifiy
    /// belgi bosardi («do'kon» o'rniga «doPkon» kabi).
    /// </summary>
    [Fact]
    public void Tipografik_apostrof_almashtiriladi()
    {
        var tspl = Build(new LabelData("G’isht qizil", "2530305808431", null));

        Assert.Contains("G'isht", tspl);
        Assert.DoesNotContain("’", tspl);
    }

    /// <summary>
    /// Qo'shtirnoq qochiriladi: aks holda u buyruqni erta tugatar va
    /// printer qolgan qismini alohida buyruq deb o'qib, yorliqni buzardi.
    /// </summary>
    [Fact]
    public void Qoshtirnoq_qochiriladi()
    {
        var tspl = Build(new LabelData("Sement \"Bekobod\"", "2530305808431", null));

        Assert.Contains("\\\"Bekobod\\\"", tspl);
    }

    /// <summary>Artikul bo'sh bo'lsa qator umuman chiqmaydi — yorliq kichik.</summary>
    [Fact]
    public void Artikulsiz_tovarda_qator_yozilmaydi()
    {
        var withSku = Build(Cement());
        var without = Build(new LabelData("Sement M400", "2530305808431", null));

        Assert.Contains("A-125", withSku);
        Assert.True(
            without.Split("\r\n").Count(l => l.StartsWith("TEXT ", StringComparison.Ordinal))
            < withSku.Split("\r\n").Count(l => l.StartsWith("TEXT ", StringComparison.Ordinal)));
    }

    // ── Kalibrovka va sinov ─────────────────────────────────────────────

    /// <summary>
    /// Kalibrovka printerga yorliq uzunligini O'ZI o'lchashni buyuradi.
    /// Haqiqiy printerda aynan shu qadam maketni joyiga qo'ydi.
    /// </summary>
    [Fact]
    public void Kalibrovka_GAPDETECT_yuboradi()
    {
        var tspl = Text(TsplLabel.Calibrate(57, 38));

        Assert.Contains("SIZE 57 mm,38 mm", tspl);
        Assert.Contains("GAPDETECT", tspl);
        // Kalibrovkada maket YO'Q: u faqat o'lchaydi.
        Assert.DoesNotContain("BARCODE", tspl);
        Assert.DoesNotContain("PRINT ", tspl);
    }

    /// <summary>
    /// Sinov yorlig'i — ramka va markaziy xoch. Bir necha millimetrlik
    /// siljish aynan shu ramkadan ko'rinadi.
    /// </summary>
    [Fact]
    public void Sinov_yorligi_ramka_chizadi()
    {
        var tspl = Text(TsplLabel.TestLabel(57, 38));

        Assert.Contains("BOX ", tspl);
        Assert.Contains("BAR ", tspl);
        Assert.Contains("PRINT 1,1", tspl);
    }

    // ── Sozlamadan o'lcham ──────────────────────────────────────────────

    /// <summary>
    /// Rulon o'lchami SO'ROVDAN emas, SOZLAMADAN olinadi.
    /// </summary>
    [Fact]
    public async Task Olcham_sozlamadan_olinadi_sorovdan_emas()
    {
        using var h = new TestHarness();
        h.Settings.GetOrCreateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MarketSettings
            {
                LabelWidthMm = 57m, LabelHeightMm = 38m, LabelGapMm = 2m, LabelOffsetMm = 2m,
            });

        var id = await SeedProductAsync(h);
        // So'rovda ATAYLAB boshqa o'lcham — u e'tiborga olinmasligi kerak.
        var result = await h.NewProductLabelService()
            .RenderLabelsTsplAsync(new PrintLabelsDto([new LabelItemDto(id)], 30, 20));

        Assert.True(result.IsSuccess, result.Error);
        var tspl = Text(result.Value);
        Assert.Contains("SIZE 57 mm,38 mm", tspl);
        Assert.DoesNotContain("SIZE 30 mm", tspl);
        Assert.Contains("SHIFT 16", tspl);   // 2 mm × 8 nuqta
    }

    /// <summary>
    /// Sozlamada NOL turgan bo'lsa printerga «SIZE 0 mm» ketmaydi.
    /// </summary>
    /// <remarks>
    /// Ko'chirish mavjud qatorlarga to'g'ri qiymat yozadi, ya'ni nolli qator
    /// bo'lmasligi kerak. Lekin <c>SIZE 0 mm</c> da printerning qanday yo'l
    /// tutishi umuman aniqlanmagan va bu chegara arzon — shuning uchun nol
    /// «sozlanmagan» deb qabul qilinadi.
    /// </remarks>
    [Fact]
    public async Task Nol_olcham_printerga_ketmaydi()
    {
        using var h = new TestHarness();
        h.Settings.GetOrCreateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MarketSettings { LabelWidthMm = 0m, LabelHeightMm = 0m });

        var id = await SeedProductAsync(h);
        var result = await h.NewProductLabelService()
            .RenderLabelsTsplAsync(new PrintLabelsDto([new LabelItemDto(id)]));

        Assert.True(result.IsSuccess, result.Error);
        var tspl = Text(result.Value);
        Assert.DoesNotContain("SIZE 0 mm", tspl);
        Assert.Contains("SIZE 58 mm,40 mm", tspl);
    }

    private static async Task<Guid> SeedProductAsync(TestHarness h)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement M400", MarketId = 1,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = 10, MinThreshold = 1, Unit = UnitType.Bag,
            Barcode = "2530305808431",
        };
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return product.Id;
    }

    // ── Umumiy ──────────────────────────────────────────────────────────

    /// <summary>
    /// Har bir buyruq O'Z qatorida tugaydi: printer buyruqlarni satr
    /// oxiri bo'yicha ajratadi va bittasi qo'shilib qolsa ikkalasi ham
    /// tashlab yuboriladi.
    /// </summary>
    [Fact]
    public void Har_buyruq_CRLF_bilan_tugaydi()
    {
        var tspl = Build(Cement());

        Assert.EndsWith("\r\n", tspl);
        Assert.DoesNotContain("\n\n", tspl);
    }
}
