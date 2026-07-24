using Buildix.Application.DTOs;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Read-side of the product domain (CQRS-style separation). Market-scoped
/// read-only projections extracted from ProductService. The <c>canViewCost</c>
/// flag masks the cost price for roles lacking data.costPrice.
/// </summary>
public interface IProductQueryService
{
    Task<ProductDto?> GetProductByIdAsync(Guid id, bool canViewCost = true, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductDto>> GetAllProductsAsync(bool canViewCost = true, CancellationToken cancellationToken = default);
    Task<PagedResult<ProductDto>> GetAllProductsPagedAsync(int page, int size, bool canViewCost = true, string? search = null, int? categoryId = null, bool lowStockOnly = false, bool includeHidden = false, CancellationToken cancellationToken = default);

    /// <summary>Bitta tovarning ombor harakatlari (eng yangisi birinchi) — "Движение товара".</summary>
    Task<IReadOnlyList<StockMovementDto>> GetProductMovementsAsync(Guid productId, int limit = 50, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductDto>> GetLowStockProductsAsync(bool canViewCost = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Warehouse KPI tiles (positions / stock value / low / out) aggregated
    /// DB-side. <paramref name="canViewCost"/> masks StockValue for cost-hidden
    /// callers.
    /// </summary>
    Task<WarehouseSummaryDto> GetWarehouseSummaryAsync(bool canViewCost = true, CancellationToken cancellationToken = default);
}
