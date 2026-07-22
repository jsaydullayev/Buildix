using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Guards the HIGH-2 fix: the AppDbContext global query filter must scope every
/// tenant-owned read to the caller's market, and disable itself when there is
/// no market context (SuperAdmin / startup). These run against the real
/// AppDbContext so a regression in the filter expression fails the build.
/// </summary>
public class TenantIsolationTests
{
    private static Product MakeProduct(int marketId, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        MarketId = marketId,
        CostPrice = 100,
        SalePrice = 150,
        MinSalePrice = 120,
        Quantity = 10,
        MinThreshold = 2,
        Unit = UnitType.Piece,
    };

    private static async Task SeedTwoMarketsAsync(TestHarness h)
    {
        // Writes are never filtered, so seed both markets from one context.
        h.Db.Products.AddRange(
            MakeProduct(1, "Market-1 Cement"),
            MakeProduct(1, "Market-1 Brick"),
            MakeProduct(2, "Market-2 Paint"));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear(); // force reads to hit the query filter, not the cache
    }

    [Fact]
    public async Task Read_returns_only_current_market_rows()
    {
        using var h = new TestHarness(marketId: 1);
        await SeedTwoMarketsAsync(h);

        var products = await h.Db.Products.ToListAsync();

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.Equal(1, p.MarketId));
    }

    [Fact]
    public async Task Row_from_another_market_is_invisible_by_id()
    {
        using var h = new TestHarness(marketId: 1);
        await SeedTwoMarketsAsync(h);
        var otherMarketProductId = h.Db.Products.IgnoreQueryFilters().Single(p => p.MarketId == 2).Id;

        // Simulate a service that forgot its manual .Where(MarketId==x): a plain
        // by-id lookup must still come back empty because of the global filter.
        var leaked = await h.Db.Products.FirstOrDefaultAsync(p => p.Id == otherMarketProductId);

        Assert.Null(leaked);
    }

    [Fact]
    public async Task Null_market_context_sees_all_markets()
    {
        // SuperAdmin / startup: no market → filter is a no-op → cross-tenant read.
        using var h = new TestHarness(marketId: null);
        await SeedTwoMarketsAsync(h);

        var products = await h.Db.Products.ToListAsync();

        Assert.Equal(3, products.Count);
    }

    [Fact]
    public async Task Switching_market_switches_the_visible_set()
    {
        using var h = new TestHarness(marketId: 1);
        await SeedTwoMarketsAsync(h);

        h.Market.MarketId = 2; // same context, different tenant on the next query
        var products = await h.Db.Products.ToListAsync();

        Assert.Single(products);
        Assert.Equal("Market-2 Paint", products[0].Name);
    }
}
