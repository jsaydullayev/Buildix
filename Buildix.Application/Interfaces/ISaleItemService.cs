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
    Task<bool> ValidateSalePriceAsync(Guid saleItemId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Edit a line price. <paramref name="userId"/> MUST be the authenticated
    /// caller's id from the JWT claim — it's the actor on the fraud-audit row.
    /// </summary>
    Task<Result<SaleItemDto>> UpdateSaleItemPriceAsync(Guid saleItemId, UpdateSaleItemPriceDto request, Guid userId, CancellationToken cancellationToken = default);
}
