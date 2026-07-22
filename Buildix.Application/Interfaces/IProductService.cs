using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

public interface IProductService
{
    // Read/query methods → IProductQueryService; image methods (SetProductImage,
    // RemoveProductImage) → IProductImageService. What remains is product
    // create / update / delete / stock mutation.
    Task<Result<ProductDto>> CreateProductAsync(CreateProductDto request, Guid? sellerId, CancellationToken cancellationToken = default);

    // canEditStock=true lets the caller hand-correct on-hand Quantity via
    // request.Quantity (Owner/SuperAdmin only — the controller derives it from
    // the role). Defaults to false so existing call-sites and every non-Owner
    // request leave stock untouched; stock otherwise moves only through zakup/sales.
    // canEditCost=true lets the caller set CostPrice via request.CostPrice
    // (Owner/Admin — cost-viewers only). Defaults to false so cost-hidden callers
    // (whose GET masks CostPrice to 0) can't clobber the stored cost on edit.
    Task<Result<ProductDto>> UpdateProductAsync(UpdateProductDto request, Guid actorUserId, bool canEditStock = false, bool canEditCost = false, CancellationToken cancellationToken = default);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> UpdateStockAsync(Guid id, decimal quantityChange, CancellationToken cancellationToken = default);

    // Bulk physical inventory count (Инвентаризация). Sets each product's on-hand
    // Quantity to the counted figure and journals the variance. Owner/SuperAdmin
    // only (fraud-sensitive) — the controller gates the role before calling.
    Task<Result<StocktakeResultDto>> StocktakeAsync(StocktakeRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
}
