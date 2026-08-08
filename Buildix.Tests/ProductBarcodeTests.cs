using Buildix.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Shtrix-kod skanerlashning shartlari.
///
/// <para>Skanerdan ko'zlangan foyda bitta: kod o'qilgach kassir hech narsa
/// tanlamasin. Buning uchun kod market ichida YAGONA bo'lishi va bazadagi
/// qiymat skaner yuborgani bilan AYNAN mos tushishi kerak. Quyidagi testlar
/// shu ikki shartni qo'riqlaydi.</para>
/// </summary>
public class ProductBarcodeTests
{
    // CreateProductDto pozitsion: name, isTemporary, salePrice, minSalePrice,
    // minThreshold, categoryId, unit, quantity, hidePriceFromSellers, costPrice.
    // Shtrix-kod undan keyin turadi, shuning uchun nom bilan beriladi.
    private static CreateProductDto NewProduct(string name = "Cement", string? barcode = null) =>
        new(name, false, 50_000, 40_000, 5, null, 1, 100, false, 30_000, Barcode: barcode);

    [Fact]
    public async Task Barcode_is_stored_without_whitespace()
    {
        using var h = new TestHarness();

        // Skanerlar ba'zan kodni bo'lib yuboradi, qo'lda kiritganda esa guruhlab
        // yozishadi. Tozalanmasa, skanerdan kelgan "4780123456789" bazadagi
        // "4780 123 456 789" bilan mos tushmay, tovar "topilmadi" bo'lib qolardi.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: " 4780 123 456 789 "), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var product = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == result.Value.Id);
        Assert.Equal("4780123456789", product.Barcode);
    }

    [Fact]
    public async Task Blank_barcode_is_stored_as_null()
    {
        using var h = new TestHarness();

        // Bo'sh satr null bo'lishi SHART: unikal indeks NULL larni hisobga
        // olmaydi, bo'sh satrlarni esa oladi — ya'ni kodsiz ikkinchi tovarni
        // saqlab bo'lmay qolardi.
        var result = await h.NewProductService().CreateProductAsync(NewProduct(barcode: "   "), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var product = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == result.Value.Id);
        Assert.Null(product.Barcode);
    }

    [Fact]
    public async Task Duplicate_barcode_in_the_same_market_is_refused()
    {
        using var h = new TestHarness();
        var first = await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000001"), sellerId: null);
        Assert.True(first.IsSuccess, first.Error);
        h.Db.ChangeTracker.Clear();

        var second = await h.NewProductService().CreateProductAsync(NewProduct("Gisht", "4780000000001"), sellerId: null);

        Assert.False(second.IsSuccess);
        Assert.Contains("4780000000001", second.Error);
    }

    [Fact]
    public async Task The_same_barcode_may_exist_in_another_market()
    {
        // Kod global emas, market ichida yagona: ikki do'kon bir xil zavod
        // tovarini sotishi mutlaqo odatiy hol.
        using var a = new TestHarness(marketId: 1);
        var first = await a.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000002"), sellerId: null);
        Assert.True(first.IsSuccess, first.Error);

        using var b = new TestHarness(marketId: 2);
        var second = await b.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000002"), sellerId: null);

        Assert.True(second.IsSuccess, second.Error);
    }

    [Fact]
    public async Task Lookup_finds_the_product_by_exact_code()
    {
        using var h = new TestHarness();
        var created = await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000003"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        var found = await h.NewProductQueryService().GetProductByBarcodeAsync("4780000000003");

        Assert.NotNull(found);
        Assert.Equal(created.Value.Id, found!.Id);
    }

    [Fact]
    public async Task Lookup_tolerates_whitespace_from_the_scanner()
    {
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000004"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        var found = await h.NewProductQueryService().GetProductByBarcodeAsync(" 4780 000 000 004 ");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task Lookup_does_not_match_a_partial_code()
    {
        // Aniq moslik, `LIKE %…%` emas: aks holda "478" ni skanerlagan kassir
        // o'nlab tovar ichidan tanlashga majbur bo'lardi.
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000005"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("478"));
        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("47800000000051"));
    }

    [Fact]
    public async Task Hidden_products_are_not_reachable_by_scanning()
    {
        // Kassa katalogida ko'rinmaydigan tovar skaner orqali ham chekka
        // tushmasligi kerak — aks holda yashirish qoidasini chetlab o'tish
        // yo'li ochilardi.
        using var h = new TestHarness();
        var created = await h.NewProductService()
            .CreateProductAsync(new CreateProductDto("Cement", false, 50_000, 40_000, 5, null, 1, 100, false, 30_000,
                Barcode: "4780000000006", IsHidden: true), sellerId: null);
        Assert.True(created.IsSuccess, created.Error);
        h.Db.ChangeTracker.Clear();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("4780000000006"));
    }

    [Fact]
    public async Task Unknown_code_returns_nothing()
    {
        using var h = new TestHarness();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("0000000000000"));
        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("   "));
    }
}
