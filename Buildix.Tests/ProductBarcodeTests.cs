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
        // yozishadi. Tozalanmasa, skanerdan kelgan "4780123456781" bazadagi
        // "4780 123 456 781" bilan mos tushmay, tovar "topilmadi" bo'lib qolardi.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: " 4780 123 456 781 "), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var product = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == result.Value.Id);
        Assert.Equal("4780123456781", product.Barcode);
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
        var first = await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000014"), sellerId: null);
        Assert.True(first.IsSuccess, first.Error);
        h.Db.ChangeTracker.Clear();

        var second = await h.NewProductService().CreateProductAsync(NewProduct("Gisht", "4780000000014"), sellerId: null);

        Assert.False(second.IsSuccess);
        Assert.Contains("4780000000014", second.Error);
    }

    [Fact]
    public async Task The_same_barcode_may_exist_in_another_market()
    {
        // Kod global emas, market ichida yagona: ikki do'kon bir xil zavod
        // tovarini sotishi mutlaqo odatiy hol.
        using var a = new TestHarness(marketId: 1);
        var first = await a.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000021"), sellerId: null);
        Assert.True(first.IsSuccess, first.Error);

        using var b = new TestHarness(marketId: 2);
        var second = await b.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000021"), sellerId: null);

        Assert.True(second.IsSuccess, second.Error);
    }

    [Fact]
    public async Task Lookup_finds_the_product_by_exact_code()
    {
        using var h = new TestHarness();
        var created = await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000038"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        var found = await h.NewProductQueryService().GetProductByBarcodeAsync("4780000000038");

        Assert.NotNull(found);
        Assert.Equal(created.Value.Id, found!.Id);
    }

    [Fact]
    public async Task Lookup_tolerates_whitespace_from_the_scanner()
    {
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000045"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        var found = await h.NewProductQueryService().GetProductByBarcodeAsync(" 4780 000 000 045 ");

        Assert.NotNull(found);
    }

    [Fact]
    public async Task Lookup_does_not_match_a_partial_code()
    {
        // Aniq moslik, `LIKE %…%` emas: aks holda "478" ni skanerlagan kassir
        // o'nlab tovar ichidan tanlashga majbur bo'lardi.
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780000000052"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("478"));
        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("47800000000521"));
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
                Barcode: "4780000000069", IsHidden: true), sellerId: null);
        Assert.True(created.IsSuccess, created.Error);
        h.Db.ChangeTracker.Clear();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("4780000000069"));
    }

    [Fact]
    public async Task Unknown_code_returns_nothing()
    {
        using var h = new TestHarness();

        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("0000000000000"));
        Assert.Null(await h.NewProductQueryService().GetProductByBarcodeAsync("   "));
    }

    // ── Kiritilgan kodning yaroqliligi ───────────────────────────────────────
    // Zavod kodini biriktirish oqimi. Ilgari har qanday satr saqlanar, xato esa
    // YORLIQ CHOP ETISHDA chiqardi — kiritilganidan ancha keyin, boshqa ekranda
    // va «noto'g'ri parametr» degan tushunarsiz xabar bilan.

    [Fact]
    public async Task Barcode_with_wrong_check_digit_is_refused_on_save()
    {
        using var h = new TestHarness();

        // 4780123456789 to'g'ri; oxirgi raqamni buzamiz.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: "4780123456788"), sellerId: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_BARCODE", result.Code);
        Assert.Contains("nazorat raqami", result.Error!);
    }

    // ── Do'konning o'z kodlari ──────────────────────────────────────────────
    // Zavod yorlig'i yo'q tovarlar ko'p, ular uchun omborchi eng oddiy raqamni
    // beradi. Bunday kod EAN-13 ga sig'maydi va Code 128 bilan bosiladi.

    [Theory]
    [InlineData("1")]
    [InlineData("12345678")]
    [InlineData("ABC-01")]
    public async Task A_shop_code_is_stored_exactly_as_typed(string typed)
    {
        using var h = new TestHarness();

        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: typed), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        // Aynan kiritilgani saqlanadi: yorliq skanerlanganda o'sha kod qaytadi.
        Assert.Equal(typed, result.Value.Barcode);
    }

    [Fact]
    public async Task A_thirteen_digit_code_with_a_broken_check_digit_is_still_refused()
    {
        using var h = new TestHarness();

        // 13 xonali raqam — bu zavod kodi da'vosi. Uni jimgina "do'kon kodi"
        // deb qabul qilish omborchini adashtirardi: u zavod yorlig'ini
        // noto'g'ri ko'chirganini bilmay qolardi.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: "4780123456789"), sellerId: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("nazorat", result.Error!);
    }

    [Fact]
    public async Task A_non_ascii_code_is_refused_before_printing()
    {
        using var h = new TestHarness();

        // Code 128 faqat ASCII ni kodlaydi — kirill kiritilsa yorliq bosishda
        // portlardi, shuning uchun kiritish paytida aytiladi.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: "Семент"), sellerId: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Upc_a_from_a_factory_label_becomes_ean13()
    {
        using var h = new TestHarness();

        // AQSh tovarlaridagi 12 xonali UPC-A. Standart bo'yicha oldiga «0»
        // qo'yilsa aynan shu tovarning EAN-13 shakli chiqadi — rad etmaymiz.
        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: "036000291452"), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var p = await h.Db.Products.IgnoreQueryFilters().FirstAsync(x => x.Id == result.Value.Id);
        Assert.Equal("0036000291452", p.Barcode);
    }

    [Fact]
    public async Task Valid_factory_ean13_is_kept_as_is()
    {
        using var h = new TestHarness();

        var result = await h.NewProductService()
            .CreateProductAsync(NewProduct(barcode: "4780123456781"), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var p = await h.Db.Products.IgnoreQueryFilters().FirstAsync(x => x.Id == result.Value.Id);
        Assert.Equal("4780123456781", p.Barcode);
    }

    [Fact]
    public async Task Printing_a_product_with_a_broken_barcode_names_the_product()
    {
        using var h = new TestHarness();

        // Tekshiruv kiritilishidan OLDIN biriktirilgan yaroqsiz kod. Bunday
        // yozuv bazada allaqachon bo'lishi mumkin, shuning uchun chop etish uni
        // portlatmasdan, qaysi tovar aybdorligini aytib to'xtashi kerak.
        var created = await h.NewProductService()
            .CreateProductAsync(NewProduct("Cement", "4780123456781"), sellerId: null);
        Assert.True(created.IsSuccess, created.Error);
        var product = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == created.Value.Id);
        product.Barcode = "4780123456789";          // nazorat raqami buzilgan
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.NewProductLabelService().RenderLabelsAsync(
            new PrintLabelsDto([new LabelItemDto(created.Value.Id, 1)], 58, 40));

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_BARCODE", result.Code);
        Assert.Contains("Cement", result.Error!);
        Assert.Contains("4780123456789", result.Error!);
    }

    // ── Ro'yxatdagi qidiruv ──────────────────────────────────────────────────
    // Tovarlar ro'yxatida turgan kassir skaner bosganda kod qidiruv maydoniga
    // tushadi. Ilgari qidiruv faqat nom va artikulni qamrar, skanerlangan kod
    // esa «topilmadi» berardi — dizaynda ham «по названию, артикулу или
    // штрих-коду» deyilgan.

    [Fact]
    public async Task Product_list_search_finds_by_full_barcode()
    {
        using var h = new TestHarness();
        var created = await h.NewProductService()
            .CreateProductAsync(NewProduct("Cement", "4780123456781"), sellerId: null);
        Assert.True(created.IsSuccess, created.Error);
        h.Db.ChangeTracker.Clear();

        var page = await h.NewProductQueryService()
            .GetAllProductsPagedAsync(1, 20, search: "4780123456781");

        var item = Assert.Single(page.Items);
        Assert.Equal(created.Value.Id, item.Id);
    }

    [Fact]
    public async Task Product_list_search_finds_by_barcode_fragment()
    {
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780123456781"), sellerId: null);
        await h.NewProductService().CreateProductAsync(NewProduct("Brick", "2011111111118"), sellerId: null);
        h.Db.ChangeTracker.Clear();

        // Skaner kodni to'liq yuboradi, lekin kassir qo'lda bir qismini ham
        // terishi mumkin — bo'lak bo'yicha ham topilsin.
        var page = await h.NewProductQueryService().GetAllProductsPagedAsync(1, 20, search: "478012");

        var item = Assert.Single(page.Items);
        Assert.Equal("Cement", item.Name);
    }

    [Fact]
    public async Task Product_list_search_still_finds_by_name_and_sku()
    {
        using var h = new TestHarness();
        await h.NewProductService().CreateProductAsync(NewProduct("Cement", "4780123456781"), sellerId: null);
        h.Db.ChangeTracker.Clear();
        var svc = h.NewProductQueryService();

        // Shtrix-kod qo'shilgani nom bo'yicha qidiruvni buzmasligi kerak.
        Assert.Single((await svc.GetAllProductsPagedAsync(1, 20, search: "cem")).Items);
        Assert.Empty((await svc.GetAllProductsPagedAsync(1, 20, search: "temir")).Items);
    }
}
