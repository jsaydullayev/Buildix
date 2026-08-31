using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// «Qarzlar» ekrani — chek kartochkalari.
///
/// <para>So'rov tovar grafigini to'liq yuklashdan proyeksiyaga ko'chirildi:
/// ilgari har bir ochiq qarzning hamma qatorlari va ularning har biri uchun
/// to'liq <c>Product</c> yozuvi tortilardi, ekranga esa faqat ikkita nom
/// chiqadi. Bu sinovlar ko'rinish AYNAN o'zgarmaganini qulflaydi.</para>
/// </summary>
public class DebtChecksTests
{
    private const int Market = 1;

    private static DebtQueryService NewService(TestHarness h) =>
        new(h.Db, h.Market, h.Clock);

    private static Sale AddDebtSale(
        TestHarness h, Customer customer, int saleNumber, decimal total, decimal paid)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = Guid.NewGuid(),
            CustomerId = customer.Id, SaleNumber = saleNumber,
            TotalAmount = total, PaidAmount = paid, Status = SaleStatus.Debt,
        };
        h.Db.Sales.Add(sale);
        h.Db.Debts.Add(new Debt
        {
            Id = Guid.NewGuid(), MarketId = Market, SaleId = sale.Id, CustomerId = customer.Id,
            TotalDebt = total, RemainingDebt = total - paid, Status = DebtStatus.Open,
        });
        return sale;
    }

    private static void AddItem(TestHarness h, Sale sale, Product? product, string? externalName, decimal qty)
        => h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id,
            ProductId = product?.Id, IsExternal = product is null,
            ExternalProductName = externalName, Quantity = qty,
            SalePrice = 10_000, CostPrice = 8_000,
        });

    /// <summary>
    /// Kartochkada birinchi ikkita tovar nomi va «+N» ko'rinadi; oddiy va
    /// tashqi tovar ikkalasi ham nomlanadi.
    /// </summary>
    [Fact]
    public async Task Kartochkada_tovar_nomlari_va_qolganlar_soni()
    {
        using var h = new TestHarness(Market);
        var cement = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement", MarketId = Market,
            CostPrice = 8_000, SalePrice = 10_000, MinSalePrice = 9_000,
            Quantity = 100, MinThreshold = 1, Unit = UnitType.Piece,
        };
        var board = new Product
        {
            Id = Guid.NewGuid(), Name = "Taxta", MarketId = Market,
            CostPrice = 8_000, SalePrice = 10_000, MinSalePrice = 9_000,
            Quantity = 100, MinThreshold = 1, Unit = UnitType.Piece,
        };
        h.Db.Products.AddRange(cement, board);

        var customer = new Customer
        {
            Id = Guid.NewGuid(), MarketId = Market, Phone = "+998901112233", FullName = "Xoshim",
        };
        h.Db.Customers.Add(customer);

        var sale = AddDebtSale(h, customer, saleNumber: 7, total: 300_000, paid: 100_000);
        AddItem(h, sale, cement, null, 3);
        AddItem(h, sale, board, null, 2);
        AddItem(h, sale, null, "Qo'shnidan g'isht", 5);   // tashqi tovar
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var check = Assert.Single(await NewService(h).GetDebtChecksAsync(null, null));

        Assert.Equal(7, check.SaleNumber);
        Assert.Equal("Xoshim", check.CustomerName);
        Assert.Equal(200_000m, check.RemainingDebt);
        // Ikkita nom + qolganlari soni.
        Assert.Equal("Sement ×3, Taxta ×2 +1", check.ItemsSummary);
        Assert.Equal(3, check.ItemCount);
        Assert.Equal(1, check.CustomerDebtCount);
    }

    /// <summary>Mijozning bir nechta qarzi bo'lsa, kartochkada soni ko'rinadi.</summary>
    [Fact]
    public async Task Bir_nechta_qarz_sanaladi()
    {
        using var h = new TestHarness(Market);
        var customer = new Customer
        {
            Id = Guid.NewGuid(), MarketId = Market, Phone = "+998901112244", FullName = "Aziz",
        };
        h.Db.Customers.Add(customer);
        AddDebtSale(h, customer, 1, 100_000, 0);
        AddDebtSale(h, customer, 2, 200_000, 0);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var checks = await NewService(h).GetDebtChecksAsync(null, null);

        Assert.Equal(2, checks.Count);
        Assert.All(checks, c => Assert.Equal(2, c.CustomerDebtCount));
        // Tovarsiz chekda xulosa bo'sh, lekin so'rov yiqilmaydi.
        Assert.All(checks, c => Assert.Equal(string.Empty, c.ItemsSummary));
    }

    /// <summary>Chek raqami bo'yicha qidiruv ishlaydi.</summary>
    [Fact]
    public async Task Chek_raqami_boyicha_qidiriladi()
    {
        using var h = new TestHarness(Market);
        var customer = new Customer
        {
            // Telefonda «22» ketma-ketligi ATAYLAB yo'q: qidiruv raqamni ham
            // telefon ichidan izlaydi, aks holda sinov o'zini aldab qo'yardi.
            Id = Guid.NewGuid(), MarketId = Market, Phone = "+998903334455", FullName = "Bek",
        };
        h.Db.Customers.Add(customer);
        AddDebtSale(h, customer, 11, 100_000, 0);
        AddDebtSale(h, customer, 22, 200_000, 0);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var found = Assert.Single(await NewService(h).GetDebtChecksAsync("22", null));
        Assert.Equal(22, found.SaleNumber);
    }
}
