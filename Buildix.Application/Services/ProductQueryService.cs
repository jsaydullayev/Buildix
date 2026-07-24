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
    private readonly ITashkentClock _clock;

    public ProductQueryService(IAppDbContext context, ICurrentMarketService currentMarketService, ITashkentClock clock)
    {
        _context = context;
        _currentMarketService = currentMarketService;
        _clock = clock;
    }

    public async Task<IReadOnlyList<StockMovementDto>> GetProductMovementsAsync(Guid productId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        limit = Math.Clamp(limit, 1, 200);

        return await _context.StockMovements
            .AsNoTracking()
            .Where(m => m.MarketId == marketId && m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new StockMovementDto(
                m.Id,
                m.Type.ToString(),
                m.Delta,
                m.ResultingQty,
                m.RefNumber,
                m.User != null ? m.User.FullName : null,
                m.Comment,
                m.CreatedAt))
            .ToListAsync(cancellationToken);
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

    public async Task<ProductStatsDto?> GetProductStatsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var exists = await _context.Products.AsNoTracking()
            .AnyAsync(p => p.Id == id && p.MarketId == marketId, cancellationToken);
        if (!exists) return null;

        // «Поставщик» + «Последний приход» — bu tovarni o'z ichiga olgan oxirgi
        // qabul qilingan postavka. Narх ko'rsatilmaydi (kassir cheklovi).
        var lastReceipt = await _context.Zakups.AsNoTracking()
            .Where(z => z.ProductId == id && z.ReceiptId != null
                && z.Receipt!.MarketId == marketId && z.Receipt.DeliveryStatus == Domain.Enums.DeliveryStatus.Accepted)
            .OrderByDescending(z => z.Receipt!.CreatedAt)
            .Select(z => new { z.Receipt!.CreatedAt, z.Receipt.ReceiptNumber, SupplierName = z.Receipt.Supplier != null ? z.Receipt.Supplier.Name : null })
            .FirstOrDefaultAsync(cancellationToken);

        // «Продано за месяц» — joriy Toshkent oyidagi sotilgan miqdor (draft/bekor emas).
        var monthStartUtc = _clock.LocalDayToUtcRange(new DateTime(_clock.TodayLocal.Year, _clock.TodayLocal.Month, 1)).UtcStart;
        var soldThisMonth = await _context.SaleItems.AsNoTracking()
            .Where(si => si.ProductId == id && si.Sale != null && si.Sale.MarketId == marketId
                && si.Sale.Status != Domain.Enums.SaleStatus.Draft && si.Sale.Status != Domain.Enums.SaleStatus.Cancelled
                && si.Sale.CreatedAt >= monthStartUtc)
            .SumAsync(si => (decimal?)si.Quantity, cancellationToken) ?? 0m;

        return new ProductStatsDto(
            lastReceipt?.SupplierName,
            lastReceipt?.CreatedAt,
            lastReceipt?.ReceiptNumber,
            soldThisMonth);
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
