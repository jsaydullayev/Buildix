using Buildix.Application.DTOs;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Item concern extracted from the former SaleService god-class: adding /
/// removing a sale line, editing a line price, and validating a line price.
/// Owns stock movement, per-market checks, the FOR UPDATE product lock and the
/// SUM-based total for the item paths.
/// </summary>
public interface ISaleItemService
{
    Task<Result<SaleItemDto>> AddSaleItemAsync(Guid saleId, AddSaleItemDto request, CancellationToken cancellationToken = default);
    Task<Result<SaleItemDto>> RemoveSaleItemAsync(Guid saleId, RemoveSaleItemDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a line to an EXACT quantity (not a delta). The register needs this:
    /// "12 qop" / "3.5 m" is one call, where Add+Remove would be a click per
    /// unit and cannot express a fraction at all. Quantity 0 removes the line.
    /// Stock is moved by the difference, so the product ends up consistent
    /// whichever direction the edit went.
    /// </summary>
    Task<Result<SaleItemDto>> SetSaleItemQuantityAsync(Guid saleId, SetSaleItemQuantityDto request, CancellationToken cancellationToken = default);
    Task<bool> ValidateSalePriceAsync(Guid saleItemId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Edit a line price. <paramref name="userId"/> MUST be the authenticated
    /// caller's id from the JWT claim — it's the actor on the fraud-audit row.
    /// </summary>
    Task<Result<SaleItemDto>> UpdateSaleItemPriceAsync(Guid saleItemId, UpdateSaleItemPriceDto request, Guid userId, CancellationToken cancellationToken = default);
}
