using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Guards the HIGH-1 fix and the core payment/debt state machine in
/// SaleService.AddPaymentAsync and DebtService.PayAsync — the money paths.
/// </summary>
public class SalePaymentTests
{
    // Seeds a Draft sale (market 1) with a single line worth unitPrice*qty.
    private static async Task<Guid> SeedSaleWithItemAsync(
        TestHarness h, decimal unitPrice, decimal qty, Guid? customerId = null)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Item", MarketId = 1,
            CostPrice = unitPrice / 2, SalePrice = unitPrice, MinSalePrice = unitPrice / 2,
            Quantity = 1000, MinThreshold = 1, Unit = UnitType.Piece,
        };
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(), CustomerId = customerId,
            Status = SaleStatus.Draft, MarketId = 1,
        };
        var item = new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = qty, SalePrice = unitPrice, CostPrice = product.CostPrice, IsExternal = false,
        };
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        h.Db.SaleItems.Add(item);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return sale.Id;
    }

    private static Task<Sale?> ReloadSaleAsync(TestHarness h, Guid saleId) =>
        h.Db.Sales.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == saleId);

    [Fact]
    public async Task Full_payment_marks_sale_paid()
    {
        using var h = new TestHarness();
        var saleId = await SeedSaleWithItemAsync(h, unitPrice: 50_000, qty: 3); // total 150_000
        var svc = h.NewSalePaymentService();

        var result = await svc.AddPaymentAsync(saleId, new AddPaymentDto("Cash", 150_000));

        Assert.True(result.IsSuccess, result.Error);
        var sale = await ReloadSaleAsync(h, saleId);
        Assert.Equal(SaleStatus.Paid, sale!.Status);
        Assert.Equal(150_000, sale.PaidAmount);
    }

    [Fact]
    public async Task Partial_payment_with_customer_creates_open_debt()
    {
        using var h = new TestHarness();
        var customerId = Guid.NewGuid();
        var saleId = await SeedSaleWithItemAsync(h, unitPrice: 50_000, qty: 2, customerId: customerId); // total 100_000
        var svc = h.NewSalePaymentService();

        var result = await svc.AddPaymentAsync(saleId, new AddPaymentDto("Cash", 40_000));

        Assert.True(result.IsSuccess, result.Error);
        var sale = await ReloadSaleAsync(h, saleId);
        Assert.Equal(SaleStatus.Debt, sale!.Status);

        var debt = await h.Db.Debts.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.SaleId == saleId);
        Assert.NotNull(debt);
        Assert.Equal(DebtStatus.Open, debt!.Status);
        Assert.Equal(60_000, debt.RemainingDebt);
    }

    [Fact]
    public async Task Overpayment_is_rejected()
    {
        using var h = new TestHarness();
        var saleId = await SeedSaleWithItemAsync(h, unitPrice: 50_000, qty: 2); // total 100_000
        var svc = h.NewSalePaymentService();

        var result = await svc.AddPaymentAsync(saleId, new AddPaymentDto("Cash", 150_000));

        Assert.True(result.IsFailure);
        var sale = await ReloadSaleAsync(h, saleId);
        Assert.NotEqual(SaleStatus.Paid, sale!.Status); // nothing was charged
    }

    [Fact]
    public async Task Partial_payment_without_customer_is_rejected()
    {
        using var h = new TestHarness();
        var saleId = await SeedSaleWithItemAsync(h, unitPrice: 50_000, qty: 2); // total 100_000, no customer
        var svc = h.NewSalePaymentService();

        var result = await svc.AddPaymentAsync(saleId, new AddPaymentDto("Cash", 40_000));

        Assert.True(result.IsFailure); // mijozsiz qarzga savdo taqiqlanadi
    }

    [Fact]
    public async Task Paying_a_debt_in_full_closes_it()
    {
        using var h = new TestHarness();
        var customerId = Guid.NewGuid();
        var saleId = await SeedSaleWithItemAsync(h, unitPrice: 50_000, qty: 2, customerId: customerId); // total 100_000
        var saleSvc = h.NewSalePaymentService();
        var partial = await saleSvc.AddPaymentAsync(saleId, new AddPaymentDto("Cash", 40_000)); // debt 60_000
        Assert.True(partial.IsSuccess, partial.Error);
        h.Db.ChangeTracker.Clear();

        var debtId = (await h.Db.Debts.IgnoreQueryFilters().FirstAsync(d => d.SaleId == saleId)).Id;
        var debtSvc = h.NewDebtService();
        var result = await debtSvc.PayAsync(debtId, new PayDebtDto(60_000, "Cash"), actorUserId: Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var debt = await h.Db.Debts.IgnoreQueryFilters().FirstAsync(d => d.Id == debtId);
        Assert.Equal(DebtStatus.Closed, debt.Status);
        Assert.Equal(0, debt.RemainingDebt);
    }
}
