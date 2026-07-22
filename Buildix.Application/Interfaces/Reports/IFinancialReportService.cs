using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// Financial rollups — profit summary (today/week/month/all-time) and the
/// cash-vs-card balance. Both are Owner-gated at the API layer.
/// </summary>
public interface IFinancialReportService
{
    Task<ProfitSummaryDto> GetProfitSummaryAsync(CancellationToken cancellationToken = default);
    Task<CashBalanceDto> GetCashBalanceAsync(CancellationToken cancellationToken = default);
}
