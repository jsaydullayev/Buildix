using ClosedXML.Excel;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Excel eksportlari.
///
/// <para>Fayl HAQIQATAN yaratiladi va qaytadan ochilib o'qiladi — ustun
/// nomlarini kod ichida tekshirish yetarli emas edi: sarlavhalar anonim
/// tipning xossa nomidan olinardi va o'sha bog'liqlik ko'rinmasdi.</para>
/// </summary>
public class ExcelExportTests
{
    private static readonly ITashkentClock Clock =
        new TashkentClock(TimeZoneInfo.CreateCustomTimeZone("TST", TimeSpan.FromHours(5), "Tashkent", "Tashkent"));

    /// <summary>Yaratilgan faylning birinchi varag'ini o'qiydi.</summary>
    private static (List<string> Headers, List<List<string>> Rows) Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(1);

        var headers = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var rows = ws.RowsUsed().Skip(1)
            .Select(r => Enumerable.Range(1, headers.Count).Select(i => r.Cell(i).GetString()).ToList())
            .ToList();
        return (headers, rows);
    }

    // ── Sotuvlar ──────────────────────────────────────────────────────────

    private static SalesExcelExportService NewSalesService(params SaleDto[] sales)
    {
        var query = Substitute.For<ISaleQueryService>();
        query.GetAllSalesAsync(Arg.Any<CancellationToken>()).Returns(sales.AsEnumerable());
        return new SalesExcelExportService(query, new ExcelService(), Clock);
    }

    private static SaleDto NewSale(int number, string status, params string[] productNames)
    {
        var items = productNames.Select((n, i) => new SaleItemDto(
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid(), n,
            Quantity: 2, CostPrice: 1000, SalePrice: 1500, TotalPrice: 3000, Profit: 1000,
            Unit: "dona", Comment: null, IsExternal: false)).ToList();

        return new SaleDto(
            Guid.NewGuid(), number, Guid.NewGuid(), "Jaxongir", null, null, null,
            status, TotalAmount: 3000 * items.Count, PaidAmount: 0, RemainingAmount: 0,
            DiscountAmount: 0, CreatedAt: new DateTime(2026, 8, 22, 15, 44, 0, DateTimeKind.Utc),
            Items: items, Payments: []);
    }

    /// <summary>
    /// Holat ilova tilida chiqishi kerak. Ilgari u bazadagi ingliz nomi
    /// ko'rinishida yozilardi — ruscha faylda ham «Paid» turardi.
    /// </summary>
    [Theory]
    [InlineData("uz", "Holat", "Qarz")]
    [InlineData("ru", "Статус", "Долг")]
    [InlineData("en", "Status", "Debt")]
    public async Task Savdo_holati_ilova_tilida_chiqadi(string lang, string header, string value)
    {
        var service = NewSalesService(NewSale(40, "Debt", "sement"));

        var file = await service.ExportSalesAsync(lang, canViewCost: true, canViewProfit: true);
        var (headers, rows) = Read(file.Content);

        Assert.Contains(header, headers);
        Assert.Equal(value, rows[0][headers.IndexOf(header)]);
    }

    /// <summary>
    /// Inglizcha ATAYLAB tekshiriladi: ilgari u hech qayerda ko'rsatilmagan
    /// va jimgina o'zbekchaga tushardi — fayl ingliz tilidagi ilovadan
    /// o'zbekcha chiqardi va buni hech narsa bildirmasdi.
    /// </summary>
    [Fact]
    public async Task Inglizcha_fayl_ozbekchaga_tushmaydi()
    {
        var service = NewSalesService(NewSale(40, "Paid", "sement"));

        var (headers, _) = Read((await service.ExportSalesAsync("en", true, true)).Content);

        Assert.Contains("Date", headers);
        Assert.DoesNotContain("Sana", headers);
    }

    /// <summary>
    /// Bitta chekda bir necha tovar bo'lishi mumkin va faylda ular alohida
    /// qator bo'lib yotadi. Chek raqamisiz ularning bitta savdo ekanini
    /// ajratib bo'lmasdi.
    /// </summary>
    [Fact]
    public async Task Bir_chekdagi_tovarlar_chek_raqami_bilan_boglanadi()
    {
        var service = NewSalesService(NewSale(40, "Paid", "sement", "gisht", "qum"));

        var (headers, rows) = Read((await service.ExportSalesAsync("uz", true, true)).Content);

        var chek = headers.IndexOf("Chek");
        Assert.True(chek >= 0, "«Chek» ustuni yo'q");
        // Sanadan KEYIN turishi kerak.
        Assert.Equal(headers.IndexOf("Sana") + 1, chek);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("40", r[chek]));
    }

    // ── Tovarlar ──────────────────────────────────────────────────────────

    private static ProductsExcelExportService NewProductsService(params ProductDto[] products)
    {
        var query = Substitute.For<IProductQueryService>();
        query.GetAllProductsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(products.AsEnumerable());
        return new ProductsExcelExportService(query, new ExcelService(), Clock);
    }

    private static ProductDto NewProduct(string name, string? sku) => new(
        Guid.NewGuid(), name, CostPrice: 1000, SalePrice: 1500, MinSalePrice: 1200,
        Quantity: 50, MinThreshold: 10, Unit: 0, UnitName: "dona",
        CategoryId: 1, CategoryName: "Qurilish", IsTemporary: true, IsInStock: true,
        IsLowStock: true, ImageUrl: null, HidePriceFromSellers: false, Sku: sku);

    /// <summary>
    /// ID — o'ttiz olti belgilik ichki identifikator, uni hech kim o'qimaydi.
    /// Uning o'rniga tovarni odam ham, ombor ham taniydigan ARTIKUL turadi.
    /// </summary>
    [Fact]
    public async Task Tovarlar_faylida_ID_emas_artikul_chiqadi()
    {
        var service = NewProductsService(NewProduct("sement", "CEM-400"));

        var (headers, rows) = Read((await service.ExportProductsAsync("uz", canViewCost: true)).Content);

        Assert.DoesNotContain("ID", headers);
        Assert.Contains("Artikul", headers);
        Assert.Equal("CEM-400", rows[0][headers.IndexOf("Artikul")]);
    }

    /// <summary>
    /// «Kam qoldi» qoldiq va eng kam chegaradan o'z-o'zidan kelib chiqadi —
    /// ikkalasi yonma-yon turibdi. «Vaqtinchalik» esa ichki bayroq.
    /// </summary>
    [Theory]
    [InlineData("uz")]
    [InlineData("ru")]
    [InlineData("en")]
    public async Task Tovarlar_faylida_ortiqcha_ustunlar_yoq(string lang)
    {
        var service = NewProductsService(NewProduct("sement", "CEM-400"));

        var (headers, _) = Read((await service.ExportProductsAsync(lang, canViewCost: true)).Content);

        foreach (var unwanted in new[]
                 {
                     "Kam_qoldi", "Kam qoldi", "Заканчивается", "Low stock",
                     "Vaqtinchalik", "Временный", "Temporary",
                 })
            Assert.DoesNotContain(unwanted, headers);
    }

    [Fact]
    public async Task Tovarlar_fayli_inglizcha_ham_chiqadi()
    {
        var service = NewProductsService(NewProduct("sement", "CEM-400"));

        var (headers, _) = Read((await service.ExportProductsAsync("en", canViewCost: true)).Content);

        Assert.Contains("SKU", headers);
        Assert.Contains("Name", headers);
        Assert.DoesNotContain("Nomi", headers);
    }

    /// <summary>Tannarx ruxsati bo'lmasa katakchada chiziqcha turishi kerak.</summary>
    [Fact]
    public async Task Tannarx_ruxsatsiz_yashiriladi()
    {
        var service = NewProductsService(NewProduct("sement", "CEM-400"));

        var (headers, rows) = Read((await service.ExportProductsAsync("uz", canViewCost: false)).Content);

        Assert.Equal("—", rows[0][headers.IndexOf("Xarid narxi")]);
    }
}
