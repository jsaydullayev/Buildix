using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

public interface IDebtService
{
    // Read/query methods (GetByCustomer, GetCustomerTotal, List) moved to
    // IDebtQueryService (CQRS-style separation). What remains is the debt
    // mutation surface.

    /// <summary>
    /// Record a payment against an open debt. Atomic: creates a Payment row,
    /// updates the debt + parent Sale, and adjusts the per-market CashRegister
    /// for cash payments. Fails with a "NOT_FOUND" Result when the debt/sale is
    /// missing; any other business failure carries a plain message (→ 400).
    /// </summary>
    Task<Result<PayDebtResultDto>> PayAsync(Guid debtId, PayDebtDto request, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Qarzning to'lov muddatini (due date) yangilaydi, tenant-scoped. Debt
    /// topilmasa "NOT_FOUND" Result qaytaradi. dueDate null bo'lsa — muddat
    /// olib tashlanadi.
    /// </summary>
    Task<Result<DebtDto>> UpdateDueDateAsync(Guid debtId, DateTime? dueDate, CancellationToken cancellationToken = default);
}
