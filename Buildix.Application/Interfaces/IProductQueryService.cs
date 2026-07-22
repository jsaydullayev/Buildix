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
    Task<PagedResult<ProductDto>> GetAllProductsPagedAsync(int page, int size, bool canViewCost = true, string? search = null, int? categoryId = null, bool lowStockOnly = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProductDto>> GetLowStockProductsAsync(bool canViewCost = true, CancellationToken cancellationToken = default);
}
