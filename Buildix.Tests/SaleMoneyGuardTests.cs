using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Pul mantig'idagi uchta teshik — auditdan keyin yopilgan.
///
/// <para>Uchalasi ham «hech qanday xato chiqmaydi, lekin pul noto'g'ri
/// joyda qoladi» turidagi xatolar edi: kassir ekranda hech narsa
/// sezmasdi.</para>
/// </summary>
public class SaleMoneyGuardTests
{
    private const int Market = 1;

    private static Sale AddSale(TestHarness h, Guid? customerId, decimal total, decimal paid, SaleStatus status)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = Guid.NewGuid(),
            CustomerId = customerId, TotalAmount = total, PaidAmount = paid, Status = status,
        };
        h.Db.Sales.Add(sale);
        return sale;
    }

    private static Product AddProduct(TestHarness h) =>
        new Product
        {
            Id = Guid.NewGuid(), Name = "Cement", MarketId = Market,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = 100, MinThreshold = 1, Unit = UnitType.Piece,
        };

    /// <summary>
    /// Ortiqcha to'langan chekni NOL to'lov bilan yopish mumkin.
    /// </summary>
    /// <remarks>
    /// Chegirma jamini to'langan summadan pastga tushirsa, qoldiq manfiy
    /// bo'ladi. Xato xabari «chekni to'lovsiz yoping» deb maslahat berardi,
    /// keyingi tekshiruvning o'zi esa aynan shu yo'lni to'sib turardi:
    /// `0 > -150000` rost. Kassirda chekni yopishning hech qanday usuli
    /// qolmasdi.
    /// </remarks>
    [Fact]
    public async Task Ortiqcha_tolangan_chek_nol_tolov_bilan_yopiladi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        h.Db.Products.Add(product);
        var sale = AddSale(h, customerId: null, total: 350_000, paid: 500_000, SaleStatus.Draft);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 7, SalePrice = 50_000, CostPrice = 30_000,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 0m, null));

        Assert.True(result.IsSuccess, result.Error);
        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(x => x.Id == sale.Id);
        Assert.Equal(SaleStatus.Paid, stored.Status);
    }

    /// <summary>
    /// Mijozsiz chekni «Qarzga» yozib bo'lmaydi.
    /// </summary>
    /// <remarks>
    /// Ilgari sotuv holati «Qarz» bo'lib qolar, `Debt` yozuvi esa
    /// yaratilmasdi (u mijoz shartiga bog'langan). Natijada tovar chiqib
    /// ketar, qarz esa «Qarzlar» ro'yxatida hech qachon ko'rinmasdi — uni
    /// kimdan undirishni hech narsa ko'rsatmasdi.
    /// </remarks>
    [Fact]
    public async Task Mijozsiz_chek_qarzga_yozilmaydi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        h.Db.Products.Add(product);
        var sale = AddSale(h, customerId: null, total: 200_000, paid: 0, SaleStatus.Draft);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 4, SalePrice = 50_000, CostPrice = 30_000,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSaleService()
            .MarkSaleAsDebtAsync(sale.Id, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.IsFailure);
        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(x => x.Id == sale.Id);
        Assert.NotEqual(SaleStatus.Debt, stored.Status);
        Assert.Empty(await h.Db.Debts.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Bekor qilingan chekda sarflangan AVANS mijozga qaytadi.
    /// </summary>
    /// <remarks>
    /// Avans — mijozning do'kondagi puli. Chek bekor qilinsa u qaytishi
    /// kerak; ilgari Credit qatori qoplanmasdan tashlab ketilardi va pul
    /// butunlay yo'qolardi.
    /// </remarks>
    [Fact]
    public async Task Bekor_qilishda_avans_qaytadi()
    {
        using var h = new TestHarness(Market);
        var customer = new Customer { Id = Guid.NewGuid(), MarketId = Market, Phone = "+998900000001" };
        h.Db.Customers.Add(customer);
        var sale = AddSale(h, customer.Id, total: 300_000, paid: 300_000, SaleStatus.Paid);
        h.Db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, MarketId = Market,
            PaymentType = PaymentType.Credit, Amount = 300_000, CreatedAt = DateTime.UtcNow,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSaleReversalService()
            .CancelSaleAsync(sale.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        // Qoplovchi manfiy qator yozilgan — avans mijozga qaytdi.
        var net = await h.Db.Payments.IgnoreQueryFilters()
            .Where(p => p.SaleId == sale.Id && p.PaymentType == PaymentType.Credit)
            .SumAsync(p => p.Amount);
        Assert.Equal(0m, net);

        // Bekor qilingan chekda pul qolmaydi.
        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(x => x.Id == sale.Id);
        Assert.Equal(SaleStatus.Cancelled, stored.Status);
        Assert.Equal(0m, stored.PaidAmount);
    }
}
