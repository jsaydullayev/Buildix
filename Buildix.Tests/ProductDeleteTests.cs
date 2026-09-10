using Buildix.Application.DTOs;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Tovarni o'chirish — «tovar yo'qoladi, tarix qoladi» qoidasi.
/// </summary>
/// <remarks>
/// <para>Bu yo'lda ilgari BIRORTA sinov yo'q edi, o'chirish esa oddiy
/// <c>Remove</c> bo'lgani uchun sotilgan tovarda tashqi kalit xatosi bilan
/// yiqilardi va foydalanuvchi 500 ko'rardi. Sinovlar shu sababdan ikki
/// tomonni ham qamrab oladi: nimalar HAQIQATAN o'chishi va nimalar
/// TEGILMASLIGI kerak.</para>
///
/// <para>Sinov jamlanmasi EF InMemory da ishlaydi va tashqi kalitlarni
/// majburlamaydi. Shuning uchun bu yerda satrlarning O'ZI tekshiriladi
/// (qator joyidami, nom joyidami), «xato tashlanmadi» emas — aks holda
/// haqiqiy bazada yiqiladigan kod ham yashil bo'lib o'tib ketardi.</para>
/// </remarks>
public class ProductDeleteTests
{
    private const int Market = 1;

    private static Product NewProduct(string name = "Cement", string? imageUrl = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MarketId = Market,
        CostPrice = 30_000,
        SalePrice = 50_000,
        MinSalePrice = 40_000,
        Quantity = 100,
        MinThreshold = 1,
        Unit = UnitType.Bag,
        ImageUrl = imageUrl,
    };

    private static Sale NewDraft() => new()
    {
        Id = Guid.NewGuid(),
        SellerId = Guid.NewGuid(),
        Status = SaleStatus.Draft,
        MarketId = Market,
    };

    private static AddSaleItemDto OrdinaryItem(Guid productId, decimal qty = 3) =>
        new(false, productId, null, null, qty, 50_000, 40_000, null);

    // ── Tarix qoladi ────────────────────────────────────────────────────

    /// <summary>
    /// Asosiy talab: sotilgan tovar o'chirilsa ham chek qatori joyida qoladi
    /// va tovar nomini KO'RSATIB turadi.
    /// </summary>
    [Fact]
    public async Task Sotilgan_tovar_ochirilganda_chek_qatori_va_nomi_qoladi()
    {
        using var h = new TestHarness();
        var product = NewProduct("Sement M400");
        var sale = NewDraft();
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var added = await h.NewSaleItemService().AddSaleItemAsync(sale.Id, OrdinaryItem(product.Id));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var deleted = await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid());

        Assert.True(deleted);

        // Chek qatori joyida va nom o'qiladi — tovar yo'q bo'lsa ham.
        var item = await h.Db.SaleItems.IgnoreQueryFilters()
            .SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal("Sement M400", item.ProductName);
        Assert.Equal(UnitType.Bag, item.ProductUnit);
        Assert.Equal(3, item.Quantity);
    }

    /// <summary>
    /// Nusxa AYNAN sotuv paytida olinadi — keyin emas. Busiz o'chirishdan
    /// keyin to'ldiradigan hech narsa qolmasdi.
    /// </summary>
    [Fact]
    public async Task Sotuv_qatori_nom_va_birlikni_darhol_nusxalaydi()
    {
        using var h = new TestHarness();
        var product = NewProduct("Taxta 15x3");
        var sale = NewDraft();
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewSaleItemService().AddSaleItemAsync(sale.Id, OrdinaryItem(product.Id));

        var item = await h.Db.SaleItems.IgnoreQueryFilters().SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal("Taxta 15x3", item.ProductName);
        Assert.Equal(UnitType.Bag, item.ProductUnit);
    }

    /// <summary>Tashqi (katalogda yo'q) tovarda ham nusxa to'ldiriladi.</summary>
    [Fact]
    public async Task Tashqi_qator_ham_nomni_nusxalaydi()
    {
        using var h = new TestHarness();
        var sale = NewDraft();
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var added = await h.NewSaleItemService().AddSaleItemAsync(
            sale.Id, new AddSaleItemDto(true, null, "Qo'shnidan g'isht", 500, 5, 1_000, 0, null));

        Assert.True(added.IsSuccess, added.Error);
        var item = await h.Db.SaleItems.IgnoreQueryFilters().SingleAsync(i => i.SaleId == sale.Id);
        Assert.Equal("Qo'shnidan g'isht", item.ProductName);
    }

    // ── Tovarning o'zi yo'qoladi ────────────────────────────────────────

    [Fact]
    public async Task Ochirilgan_tovar_katalogdan_yoqoladi()
    {
        using var h = new TestHarness();
        var product = NewProduct();
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid());

        // Odatdagi so'rov uni umuman ko'rmaydi — katalog, qidiruv, ro'yxatlar
        // va hisobotlar hammasi shu filtrdan o'tadi.
        Assert.False(await h.Db.Products.AnyAsync(p => p.Id == product.Id));

        var row = await h.Db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product.Id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
    }

    // ── Yordamchi ma'lumot haqiqatan o'chadi ────────────────────────────

    [Fact]
    public async Task Ochirish_ombor_harakatlarini_olib_tashlaydi()
    {
        using var h = new TestHarness();
        var product = NewProduct();
        h.Db.Products.Add(product);
        h.Db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            MarketId = Market,
            Type = StockMovementType.InitialStock,
            Delta = 100,
            ResultingQty = 100,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid());

        Assert.Empty(await h.Db.StockMovements.IgnoreQueryFilters()
            .Where(m => m.ProductId == product.Id).ToListAsync());
    }

    [Fact]
    public async Task Ochirish_rasm_faylini_ham_ochiradi()
    {
        using var h = new TestHarness();
        var product = NewProduct(imageUrl: "/uploads/products/1/abc.webp");
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid());

        await h.ImageStorage.Received(1)
            .DeleteAsync("/uploads/products/1/abc.webp", Arg.Any<CancellationToken>());
    }

    // ── Iz qoladi ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ochirish_auditga_yoziladi()
    {
        using var h = new TestHarness();
        var product = NewProduct();
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var actor = Guid.NewGuid();

        await h.NewProductService().DeleteProductAsync(product.Id, actor);

        await h.Audit.Received(1).LogActionAsync(
            AuditEntityTypes.Product, product.Id, AuditActions.Delete, actor,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // ── Chegara holatlari ───────────────────────────────────────────────

    [Fact]
    public async Task Yoq_tovarni_ochirish_false_qaytaradi()
    {
        using var h = new TestHarness();

        Assert.False(await h.NewProductService().DeleteProductAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    /// <summary>
    /// Ikkinchi urinish topmaydi — tovar allaqachon filtrdan chiqqan.
    /// Foydalanuvchi 404 oladi, xato emas.
    /// </summary>
    [Fact]
    public async Task Ikki_marta_ochirilsa_ikkinchisi_topilmaydi()
    {
        using var h = new TestHarness();
        var product = NewProduct();
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        Assert.True(await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid()));
        h.Db.ChangeTracker.Clear();
        Assert.False(await h.NewProductService().DeleteProductAsync(product.Id, Guid.NewGuid()));
    }

    /// <summary>Boshqa do'konning tovarini o'chirib bo'lmaydi.</summary>
    [Fact]
    public async Task Boshqa_market_tovari_ochirilmaydi()
    {
        using var h = new TestHarness(marketId: Market);
        var foreign = NewProduct();
        foreign.MarketId = Market + 1;
        h.Db.Products.Add(foreign);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        Assert.False(await h.NewProductService().DeleteProductAsync(foreign.Id, Guid.NewGuid()));

        var row = await h.Db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == foreign.Id);
        Assert.False(row.IsDeleted);
    }
}
