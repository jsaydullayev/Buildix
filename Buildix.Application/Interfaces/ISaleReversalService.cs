using Buildix.Application.DTOs;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Reversal concern extracted from the former SaleService god-class: unwinding a
/// sale or a line — cancel a paid sale, delete a draft/paid sale, or return a
/// line (partial/full). All of these return stock to inventory and reconcile
/// cash / debt, so they share one focused home.
/// </summary>
public interface ISaleReversalService
{
    /// <summary>
    /// Cancel a sale. <paramref name="adminId"/> MUST be the authenticated
    /// caller's id from the JWT claim — never a client-supplied value, or the
    /// resulting audit row can be forged to blame another admin.
    /// </summary>
    Task<Result<SaleDto>> CancelSaleAsync(Guid saleId, Guid adminId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete a sale (reverses stock + cash). <paramref name="userId"/> MUST
    /// be the authenticated caller's id from the JWT claim — it's the actor on
    /// the fraud-audit row, never a client-supplied value.
    /// </summary>
    /// <param name="requireOwnDraftOf">
    /// When set, the delete only succeeds if the sale is still a Draft created by
    /// this user — the narrow path for a cashier discarding their own parked
    /// receipt without holding <c>sales.delete</c>. Null = unrestricted.
    /// </param>
    Task<Result<SaleDto>> DeleteSaleAsync(Guid saleId, Guid userId, Guid? requireOwnDraftOf = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a line (partial/full), refunding money + returning stock.
    /// <paramref name="userId"/> MUST be the authenticated caller's id from the
    /// JWT claim — it's the actor on the fraud-audit row.
    /// </summary>
    Task<SaleItemDto?> ReturnSaleItemAsync(Guid saleId, ReturnSaleItemRequest request, Guid userId, CancellationToken cancellationToken = default);
}
