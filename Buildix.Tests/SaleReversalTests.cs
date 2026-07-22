using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Covers the reversal concern (cancel / delete a sale) — the stock must be
/// returned to inventory. Behaviour that must survive the SaleReversalService
/// split. Built through the real item/payment flows for realistic state.
/// </summary>
public class SaleReversalTests
{
    private static async Task<(Guid saleId, Guid productId)> SeedDraftAndProductAsync(TestHarness h, decimal stock)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Cement", MarketId = 1,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = stock, MinThreshold = 1, Unit = UnitType.Piece,
        };
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SaleStatus.Draft, MarketId = 1,
        };
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return (sale.Id, product.Id);
    }

    private static AddSaleItemDto Item(Guid productId, decimal qty) =>
        new(false, productId, null, null, qty, 50_000, 40_000, null);

    private static Task<decimal> StockAsync(TestHarness h, Guid productId) =>
        h.Db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.Quantity).FirstAsync();

    [Fact]
    public async Task Deleting_a_draft_sale_returns_stock()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        await h.NewSaleItemService().AddSaleItemAsync(saleId, Item(productId, qty: 3));
        Assert.Equal(97, await StockAsync(h, productId));
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSaleReversalService().DeleteSaleAsync(saleId, userId: Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(100, await StockAsync(h, productId)); // stock returned
    }

    [Fact]
    public async Task Cancelling_a_paid_sale_returns_stock_and_marks_cancelled()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        await h.NewSaleItemService().AddSaleItemAsync(saleId, Item(productId, qty: 3)); // total 150 000
        var paid = await h.NewSalePaymentService().AddPaymentAsync(saleId, new AddPaymentDto("Cash", 150_000));
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(97, await StockAsync(h, productId));
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSaleReversalService().CancelSaleAsync(saleId, adminId: Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(100, await StockAsync(h, productId)); // stock returned
        var sale = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
        Assert.Equal(SaleStatus.Cancelled, sale.Status);
    }
}
