using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

public interface ISaleService
{
    // Read/query methods moved to ISaleQueryService (CQRS-style separation):
    // GetSaleById, GetAllSales, GetSalesPaged, GetSalesByDateRange,
    // GetDraftSalesBySeller, GetUnfinishedSalesBySeller, GetDebtors.
    // Item methods → ISaleItemService; AddPayment → ISalePaymentService;
    // reversal (CancelSale, DeleteSale, ReturnSaleItem) → ISaleReversalService —
    // each concern extracted from this god-class. What remains is the
    // sale-lifecycle core.
    Task<Result<SaleDto>> CreateSaleAsync(CreateSaleDto request, Guid sellerId, CancellationToken cancellationToken = default);
    Task<Result<SaleDto>> UpdateSaleCustomerAsync(Guid saleId, UpdateSaleCustomerDto request, CancellationToken cancellationToken = default);

    // Customer credit application
    /// <summary>
    /// Applies customer's available credit (from negative payments/refunds) to a sale.
    /// </summary>
    Task<Result<SaleDto>> ApplyCustomerCreditAsync(Guid saleId, CancellationToken cancellationToken = default);

    // Additional methods for sale management
    /// <summary>
    /// Marks a sale as debt status
    /// </summary>
    // dueDate — "Qarzga olish"da tanlangan to'lov muddati (ixtiyoriy).
    // userId — audit aktori (JWT claim), qarzga o'tkazishni kim tasdiqlagani.
    Task<Result<SaleDto>> MarkSaleAsDebtAsync(Guid saleId, Guid userId, DateTime? dueDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a sale-level chegirma (skidka) on a Draft/Debt sale. Reduces the
    /// charged TotalAmount (gross item sum − discount, clamped at 0) without
    /// touching item SalePrices, and re-syncs any open debt against the new total.
    /// <paramref name="userId"/> is the audit actor (JWT claim).
    /// </summary>
    Task<Result<SaleDto>> SetSaleDiscountAsync(Guid saleId, decimal discountAmount, Guid userId, CancellationToken cancellationToken = default);
}
