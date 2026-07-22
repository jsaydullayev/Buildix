using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Covers the item concern (add / remove line) — stock decrement & restore and
/// SUM-based total, the behaviour that must survive the SaleItemService split.
/// </summary>
public class SaleItemTests
{
    private static async Task<(Guid saleId, Guid productId)> SeedDraftAndProductAsync(
        TestHarness h, decimal stock)
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

    private static AddSaleItemDto OrdinaryItem(Guid productId, decimal qty, decimal price = 50_000) =>
        new(false, productId, null, null, qty, price, 40_000, null);

    private static Task<decimal> StockAsync(TestHarness h, Guid productId) =>
        h.Db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.Quantity).FirstAsync();

    private static Task<decimal> SaleTotalAsync(TestHarness h, Guid saleId) =>
        h.Db.Sales.IgnoreQueryFilters().Where(s => s.Id == saleId).Select(s => s.TotalAmount).FirstAsync();

    [Fact]
    public async Task Adding_an_item_decrements_stock_and_sets_total()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);

        var result = await h.NewSaleItemService().AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 3));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(97, await StockAsync(h, productId));       // 100 − 3
        Assert.Equal(150_000, await SaleTotalAsync(h, saleId)); // 3 × 50 000
    }

    [Fact]
    public async Task Adding_more_than_stock_is_rejected_and_leaves_stock_intact()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 5);

        var result = await h.NewSaleItemService().AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 10));

        Assert.True(result.IsFailure);
        Assert.Equal(5, await StockAsync(h, productId)); // unchanged
    }

    [Fact]
    public async Task Removing_an_item_restores_stock()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 4));
        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal(96, await StockAsync(h, productId));
        h.Db.ChangeTracker.Clear();

        var removed = await svc.RemoveSaleItemAsync(saleId, new RemoveSaleItemDto(added.Value.Id, 4));

        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal(100, await StockAsync(h, productId)); // stock restored
    }
}
