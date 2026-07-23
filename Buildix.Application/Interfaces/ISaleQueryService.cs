using Buildix.Application.DTOs;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Read-side of the sales domain (CQRS-style query separation). All methods are
/// market-scoped, read-only projections to DTOs — extracted from the former
/// SaleService god-class so the write/state-machine logic and the query logic
/// each have a focused home.
/// </summary>
public interface ISaleQueryService
{
    Task<SaleDto?> GetSaleByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// All sales for the current market — Excel export only. Capped internally
    /// at 10 000 rows; for paged API consumption use <see cref="GetSalesPagedAsync"/>.
    /// </summary>
    Task<IEnumerable<SaleDto>> GetAllSalesAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<SaleDto>> GetSalesPagedAsync(int page, int size, string? search = null, Guid? sellerId = null, string? paymentType = null, string? status = null, DateTime? from = null, DateTime? to = null, Guid? shiftId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<SaleDto>> GetSalesByDateRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default);
    // sellerId null → whole-shop (all sellers'), for seller collaboration with
    // the data.allSalesView permission; otherwise only that seller's rows.
    Task<IEnumerable<SaleDto>> GetDraftSalesBySellerAsync(Guid? sellerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<SaleDto>> GetUnfinishedSalesBySellerAsync(Guid? sellerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomerDto>> GetDebtorsAsync(CancellationToken cancellationToken = default);
}
