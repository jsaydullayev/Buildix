using Buildix.Application.DTOs;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Ombor jurnali bilan qoldiq AYNAN mos kelishi kerak.
///
/// <para><b>Nega bu sinovlar bor.</b> Bugun haqiqat manbai —
/// <c>Product.Quantity</c> ustuni, jurnal esa uni tavsiflaydi. Ikkita
/// mustaqil kassa bir do'kon nomidan ishlay boshlaganda bu ishlamaydi:
/// bulut qatorni ID bo'yicha ustiga yozadi va arifmetika yo'qoladi — 3
/// sotgan va 2 sotgan kassadan 5 emas, oxirgi yuborganning raqami qoladi.
/// Jurnal esa qo'shiladigan (append-only): ikkala kassaning qatorlari
/// shunchaki qo'shiladi va hech qanday nizo qoidasi kerak emas.</para>
///
/// <para>Shu ko'chishning birinchi sharti — jurnal TO'LIQ bo'lishi.
/// Qoldiqni jurnalsiz o'zgartiradigan bitta yo'l qolsa ham, kelajakdagi
/// birlashtirish jimgina noto'g'ri raqam beradi. Quyidagi sinovlar har bir
/// yo'lni haqiqiy xizmatlar orqali haydab, qoidani tekshiradi.</para>
///
/// <para><b>Qoida sodda «Quantity == SUM(Delta)» EMAS.</b> Savat qurishda
/// qoldiq kamayadi, lekin jurnalga yozilmaydi — qoralama churn'i (qo'shdi,
/// o'chirdi, yana qo'shdi) tarixni ifloslantirmasligi kerak. Jurnalga bitta
/// yozuv chek YAKUNLANGANDA tushadi. Shuning uchun:</para>
///
/// <code>Quantity == SUM(jurnal Delta) − (ochiq qoralamalar ushlagan miqdor)</code>
/// </summary>
public class StockLedgerInvariantTests
{
    private const int Market = 1;

    /// <summary>Qoidani buzgan tovar bo'lmasligi kerak.</summary>
    private static async Task AssertNoDriftAsync(TestHarness h)
    {
        h.Db.ChangeTracker.Clear();
        var drifts = await new StockReconciler(h.Db).FindDriftAsync(Market);

        Assert.True(drifts.Count == 0, drifts.Count == 0 ? "" : string.Join("; ", drifts.Select(d =>
            $"«{d.ProductName}»: saqlangan={d.Stored}, jurnal={d.FromLedger}, "
            + $"qoralamada={d.Reserved}, farq={d.Drift}")));
    }

    private static Product AddProduct(TestHarness h, decimal quantity = 100m)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement", MarketId = Market,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = quantity, MinThreshold = 1, Unit = UnitType.Piece,
        };
        h.Db.Products.Add(product);

        // Boshlang'ich qoldiq — jurnalning birinchi qatori. ProductService
        // tovar yaratganda aynan shuni yozadi.
        h.Db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(), MarketId = Market, ProductId = product.Id,
            Type = StockMovementType.InitialStock, Delta = quantity,
            ResultingQty = quantity, CreatedAt = DateTime.UtcNow,
        });
        return product;
    }

    private static Sale AddDraft(TestHarness h, Guid? customerId = null)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = Guid.NewGuid(),
            CustomerId = customerId, SaleNumber = 1, Status = SaleStatus.Draft,
        };
        h.Db.Sales.Add(sale);
        return sale;
    }

    private static AddSaleItemDto Line(Product p, decimal qty) =>
        new(false, p.Id, null, null, qty, p.SalePrice, p.MinSalePrice, null);

    /// <summary>
    /// Savatga qo'shish qoldiqni kamaytiradi, lekin jurnalga yozmaydi —
    /// farq ochiq qoralama hisobiga qoplanadi.
    /// </summary>
    [Fact]
    public async Task Savatga_qoshish_qoidani_buzmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var added = await h.NewSaleItemService().AddSaleItemAsync(sale.Id, Line(product, 4m));
        Assert.True(added.IsSuccess, added.Error);

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(96m, stored.Quantity);
        await AssertNoDriftAsync(h);
    }

    /// <summary>Savatdan olib tashlash qoldiqni qaytaradi.</summary>
    [Fact]
    public async Task Savatdan_olib_tashlash_qoidani_buzmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var items = h.NewSaleItemService();
        var added = await items.AddSaleItemAsync(sale.Id, Line(product, 6m));
        var itemId = added.Value.Id;

        var removed = await items.RemoveSaleItemAsync(sale.Id, new RemoveSaleItemDto(itemId, 2m));
        Assert.True(removed.IsSuccess, removed.Error);

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(96m, stored.Quantity);
        await AssertNoDriftAsync(h);
    }

    /// <summary>Aniq miqdor qo'yish — kassaning asosiy yo'li.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(0)]
    public async Task Miqdorni_ozgartirish_qoidani_buzmaydi(int newQuantity)
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var items = h.NewSaleItemService();
        var added = await items.AddSaleItemAsync(sale.Id, Line(product, 5m));

        var changed = await items.SetSaleItemQuantityAsync(
            sale.Id, new SetSaleItemQuantityDto(added.Value.Id, newQuantity));
        Assert.True(changed.IsSuccess, changed.Error);

        await AssertNoDriftAsync(h);
    }

    /// <summary>
    /// Chek YAKUNLANGANDA jurnalga yozuv tushadi va qoralama zaxirasi
    /// bo'shaydi — qoida ikkala tomondan ham saqlanadi.
    /// </summary>
    [Fact]
    public async Task Chek_yakunlanganda_qoida_saqlanadi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewSaleItemService().AddSaleItemAsync(sale.Id, Line(product, 3m));

        var paid = await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 150_000m, null));
        Assert.True(paid.IsSuccess, paid.Error);

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(97m, stored.Quantity);

        // Endi jurnal to'liq: 100 (boshlang'ich) − 3 (sotuv) = 97, zaxira yo'q.
        var ledger = await h.Db.StockMovements.IgnoreQueryFilters()
            .Where(m => m.ProductId == product.Id).SumAsync(m => m.Delta);
        Assert.Equal(97m, ledger);
        await AssertNoDriftAsync(h);
    }

    /// <summary>Yakunlangan chekni bekor qilish tovarni omborga qaytaradi.</summary>
    [Fact]
    public async Task Bekor_qilish_qoidani_buzmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewSaleItemService().AddSaleItemAsync(sale.Id, Line(product, 3m));
        await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 150_000m, null));

        var cancelled = await h.NewSaleReversalService()
            .CancelSaleAsync(sale.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.Error);

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(100m, stored.Quantity);
        await AssertNoDriftAsync(h);
    }

    /// <summary>Tovar qaytarilsa u omborga qaytadi va jurnalga yoziladi.</summary>
    [Fact]
    public async Task Qaytarish_qoidani_buzmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = AddDraft(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var added = await h.NewSaleItemService().AddSaleItemAsync(sale.Id, Line(product, 5m));
        await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 250_000m, null));

        var returned = await h.NewSaleReturnService().CreateReturnAsync(
            new CreateReturnDto(sale.Id, "Defect", "Cash", null,
                [new CreateReturnLineDto(Guid.Parse(added.Value.Id), 2m)]),
            Guid.NewGuid());
        Assert.True(returned.IsSuccess, returned.Error);

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(97m, stored.Quantity);
        await AssertNoDriftAsync(h);
    }

    /// <summary>
    /// Qoida buzilsa tekshiruv buni KO'RADI.
    /// </summary>
    /// <remarks>
    /// <para>Busiz yuqoridagi sinovlar qadr-qimmatsiz bo'lardi: hech qachon
    /// yiqilmaydigan tekshiruv «hammasi joyida» deb turaveradi va jurnalni
    /// chetlab o'tadigan yangi yo'l qo'shilsa ham hech narsa sezilmasdi.
    /// Bu sinov aynan shu holatni — qoldiq jurnalsiz o'zgarganini —
    /// yasab, topilishini talab qiladi.</para>
    /// </remarks>
    [Fact]
    public async Task Jurnalsiz_ozgarish_topiladi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // Ikkinchi kassa bulutga o'z qoldig'ini yozib yuborgandek: qator
        // o'zgardi, jurnalda esa iz yo'q.
        var row = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product.Id);
        row.Quantity = 73m;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var drifts = await new StockReconciler(h.Db).FindDriftAsync(Market);

        var drift = Assert.Single(drifts);
        Assert.Equal(product.Id, drift.ProductId);
        Assert.Equal(73m, drift.Stored);
        Assert.Equal(100m, drift.FromLedger);
        Assert.Equal(0m, drift.Reserved);
        Assert.Equal(-27m, drift.Drift);
    }

    /// <summary>
    /// Inventarizatsiya (sanab chiqish) — qoldiq QO'LDA qo'yiladi va farq
    /// jurnalga «tuzatish» bo'lib tushishi kerak.
    /// </summary>
    [Fact]
    public async Task Inventarizatsiya_qoidani_buzmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var counted = await h.NewProductService().StocktakeAsync(
            new StocktakeRequest([new StocktakeItem(product.Id, 88m)]), Guid.NewGuid());
        Assert.True(counted.IsSuccess, counted.Error);

        await AssertNoDriftAsync(h);
    }
}
