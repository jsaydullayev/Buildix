using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// Covers the read concern extracted into SaleQueryService — market-scoped
/// projection and tenant isolation flowing through the query path.
/// </summary>
public class SaleQueryTests
{
    private static async Task<Guid> SeedSaleAsync(TestHarness h, int marketId)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(),
            Status = SaleStatus.Draft, MarketId = marketId,
        };
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return sale.Id;
    }

    [Fact]
    public async Task GetSaleById_returns_own_market_sale()
    {
        using var h = new TestHarness(marketId: 1);
        var saleId = await SeedSaleAsync(h, marketId: 1);

        var dto = await h.NewSaleQueryService().GetSaleByIdAsync(saleId);

        Assert.NotNull(dto);
        Assert.Equal(saleId, dto!.Id);
    }

    [Fact]
    public async Task GetSaleById_hides_other_market_sale()
    {
        using var h = new TestHarness(marketId: 1);
        var otherMarketSaleId = await SeedSaleAsync(h, marketId: 2);

        // Caller is market 1; the query service must not surface market 2's sale.
        var dto = await h.NewSaleQueryService().GetSaleByIdAsync(otherMarketSaleId);

        Assert.Null(dto);
    }
}
