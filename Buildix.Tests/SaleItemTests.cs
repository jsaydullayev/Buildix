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

    // ── set-exact-quantity (the register's decimal quantity field) ──────────

    [Fact]
    public async Task Setting_a_larger_quantity_takes_only_the_difference_from_stock()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 2));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 30));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(30, result.Value.Quantity);
        Assert.Equal(70, await StockAsync(h, productId));         // 100 − 30, not 100 − 2 − 30
        Assert.Equal(1_500_000, await SaleTotalAsync(h, saleId)); // 30 × 50 000
    }

    [Fact]
    public async Task Setting_a_smaller_quantity_gives_the_difference_back()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 20));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 5));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(95, await StockAsync(h, productId));       // 20 taken, 15 returned
        Assert.Equal(250_000, await SaleTotalAsync(h, saleId)); // 5 × 50 000
    }

    [Fact]
    public async Task A_fractional_quantity_is_stored_and_priced_as_typed()
    {
        using var h = new TestHarness();
        // The shop sells m / kg / tonna — "3.5" has to survive the round trip,
        // which the old click-per-unit stepper could not even express.
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 10);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 1));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 3.5m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3.5m, result.Value.Quantity);
        Assert.Equal(6.5m, await StockAsync(h, productId));     // 10 − 3.5
        Assert.Equal(175_000, await SaleTotalAsync(h, saleId)); // 3.5 × 50 000
    }

    [Fact]
    public async Task Setting_a_quantity_above_stock_is_rejected_and_leaves_the_line_intact()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 10);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 4));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 50));

        Assert.True(result.IsFailure);
        Assert.Equal(6, await StockAsync(h, productId));        // still just the original 4 taken
        Assert.Equal(200_000, await SaleTotalAsync(h, saleId)); // 4 × 50 000
    }

    [Fact]
    public async Task Setting_the_quantity_of_an_external_line_moves_no_stock()
    {
        using var h = new TestHarness();
        // The external branch skips the product lock entirely, so it also skips
        // the post-lock quantity re-read — worth pinning that it still prices
        // correctly and leaves the catalogue alone.
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var external = new AddSaleItemDto(true, null, "Yetkazib berish", 10_000m, 1, 80_000m, 0, null);
        var added = await svc.AddSaleItemAsync(saleId, external);
        Assert.True(added.IsSuccess, added.Error);

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 2.5m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2.5m, result.Value.Quantity);
        Assert.Equal(100, await StockAsync(h, productId));      // untouched
        Assert.Equal(200_000, await SaleTotalAsync(h, saleId)); // 2.5 × 80 000
    }

    [Fact]
    public async Task Setting_the_same_quantity_again_moves_no_stock()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 6));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 6));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(94, await StockAsync(h, productId)); // still just the one deduction
    }

    [Fact]
    public async Task Setting_the_quantity_to_zero_drops_the_line_and_restores_stock()
    {
        using var h = new TestHarness();
        var (saleId, productId) = await SeedDraftAndProductAsync(h, stock: 100);
        var svc = h.NewSaleItemService();
        var added = await svc.AddSaleItemAsync(saleId, OrdinaryItem(productId, qty: 7));
        Assert.True(added.IsSuccess, added.Error);
        h.Db.ChangeTracker.Clear();

        var result = await svc.SetSaleItemQuantityAsync(saleId, new SetSaleItemQuantityDto(added.Value.Id, 0));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(100, await StockAsync(h, productId));
        Assert.Equal(0, await SaleTotalAsync(h, saleId));
        Assert.False(await h.Db.SaleItems.IgnoreQueryFilters().AnyAsync(si => si.SaleId == saleId));
    }
}
