using Buildix.Application.DTOs;
using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Read-side of the debt domain (CQRS-style query separation). Market-scoped,
/// read-only projections extracted from DebtService so the debt-payment
/// state machine and the queries each have a focused home.
/// </summary>
public interface IDebtQueryService
{
    Task<IEnumerable<DebtDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<decimal> GetCustomerTotalAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DebtDto>> ListAsync(DebtStatus? status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Долги ekrani uchun mijoz bo'yicha jamlangan qarzdorlar ro'yxati.
    /// <paramref name="search"/> — ism/telefon; <paramref name="due"/> —
    /// "overdue" | "today" | "upcoming" (null = hammasi).
    /// </summary>
    Task<IReadOnlyList<DebtorSummaryDto>> GetDebtorSummariesAsync(string? search, string? due, CancellationToken cancellationToken = default);

    /// <summary>Долги sarlavhasidagi stat kartalar (jami/просрочено/платежи).</summary>
    Task<DebtSummaryStatsDto> GetSummaryStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>«Принятые сегодня» — bugun qabul qilingan qarz-to'lovlari (yangi first).</summary>
    Task<IReadOnlyList<DebtPaymentTodayDto>> GetTodayPaymentsAsync(CancellationToken cancellationToken = default);
}
