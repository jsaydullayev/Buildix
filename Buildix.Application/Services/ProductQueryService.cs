using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Common;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Read-side of the product domain. Market-scoped, read-only projections.
/// See <see cref="IProductQueryService"/>. Mapping via <see cref="ProductMapper"/>.
/// </summary>
public class ProductQueryService : IProductQueryService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;

    public ProductQueryService(IAppDbContext context, ICurrentMarketService currentMarketService)
    {
        _context = context;
        _currentMarketService = currentMarketService;
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id, bool canViewCost = true, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // P3 — pure read-then-map path. Don't pay the change-tracker cost
        // (snapshot of every loaded property + reverse navigation fix-up)
        // on a single-entity lookup that only serves a DTO.
        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.MarketId == marketId, cancellationToken);

        if (product is null)
            return null;

        return ProductMapper.MapToDto(product, canViewCost);
    }

    public async Task<IEnumerable<ProductDto>> GetAllProductsAsync(bool canViewCost = true, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // P3 — list-then-map path; same reasoning as GetProductByIdAsync.
        // Hard cap at 5000 to prevent OOM on large markets; callers needing
        // unbounded access should use GetAllProductsPagedAsync instead.
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.MarketId == marketId)
            .Take(5000)
            .ToListAsync(cancellationToken);

        return products.Select(p => ProductMapper.MapToDto(p, canViewCost));
    }

    public async Task<PagedResult<ProductDto>> GetAllProductsPagedAsync(int page, int size, bool canViewCost = true, string? search = null, int? categoryId = null, bool lowStockOnly = false, bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var marketId = _currentMarketService.GetCurrentMarketId();

        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.MarketId == marketId);

        // Yashirilgan tovarlar sotuvchi katalogi va POS'dan chiqarib tashlanadi.
        // Admin Товары/Склад ekranlari includeHidden=true bilan hammasini ko'radi.
        // Default FALSE — shu tufayli kassa (POS) qidiruvi hech qanday
        // o'zgarishsiz yashirilganlarni ko'rsatmaydi.
        if (!includeHidden)
            query = query.Where(p => !p.IsHidden);

        // Server-side filters (Склад: qidiruv nomi/artikuli, kategoriya, "faqat tugayotgan").
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                (p.Sku != null && p.Sku.ToLower().Contains(term)));
        }
        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);
        if (lowStockOnly)
            query = query.Where(p => p.Quantity <= p.MinThreshold);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return PagedResult<ProductDto>.From(items.Select(p => ProductMapper.MapToDto(p, canViewCost)).ToList(), page, size, total);
    }

    public async Task<IEnumerable<ProductDto>> GetLowStockProductsAsync(bool canViewCost = true, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // P3 — dashboard widget; reads only.
        var products = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.MarketId == marketId && p.Quantity <= p.MinThreshold)
            .ToListAsync(cancellationToken);

        return products.Select(p => ProductMapper.MapToDto(p, canViewCost));
    }

    public async Task<WarehouseSummaryDto> GetWarehouseSummaryAsync(bool canViewCost = true, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // One grouped aggregation → a single SQL round-trip, instead of pulling
        // the whole catalogue to the client and folding it there.
        var agg = await _context.Products
            .AsNoTracking()
            .Where(p => p.MarketId == marketId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Positions = g.Count(),
                StockValue = g.Sum(p => p.CostPrice * p.Quantity),
                LowStock = g.Count(p => p.Quantity > 0 && p.Quantity <= p.MinThreshold),
                OutOfStock = g.Count(p => p.Quantity <= 0),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new WarehouseSummaryDto(
            Positions: agg?.Positions ?? 0,
            StockValue: canViewCost ? (agg?.StockValue ?? 0m) : null,
            LowStock: agg?.LowStock ?? 0,
            OutOfStock: agg?.OutOfStock ?? 0);
    }
}
