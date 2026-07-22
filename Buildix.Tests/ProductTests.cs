using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Covers ProductService mutations that stay after the query/image split:
/// create (market-scoped) and stock adjustment.
/// </summary>
public class ProductTests
{
    private static CreateProductDto NewProduct(string name = "Cement", decimal qty = 100) =>
        new(name, false, 50_000, 40_000, 5, null, 1, qty, false, 30_000);

    private static Task<decimal> StockAsync(TestHarness h, Guid id) =>
        h.Db.Products.IgnoreQueryFilters().Where(p => p.Id == id).Select(p => p.Quantity).FirstAsync();

    [Fact]
    public async Task Creating_a_product_stores_it_in_the_current_market()
    {
        using var h = new TestHarness(marketId: 7);

        var result = await h.NewProductService().CreateProductAsync(NewProduct(), sellerId: null);

        Assert.True(result.IsSuccess, result.Error);
        var product = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == result.Value.Id);
        Assert.Equal(7, product.MarketId);
        Assert.Equal(100, product.Quantity);
    }

    [Fact]
    public async Task UpdateStock_applies_the_delta()
    {
        using var h = new TestHarness();
        var created = await h.NewProductService().CreateProductAsync(NewProduct(qty: 50), sellerId: null);
        var id = created.Value.Id;
        h.Db.ChangeTracker.Clear();

        var ok = await h.NewProductService().UpdateStockAsync(id, quantityChange: 30);

        Assert.True(ok);
        Assert.Equal(80, await StockAsync(h, id)); // 50 + 30
    }
}
