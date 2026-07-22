using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// Daily / period / comprehensive sales reports and the per-day sale-item
/// breakdown. Split out of the former monolithic <c>IReportService</c>.
/// </summary>
public interface ISalesReportService
{
    Task<DailyReportDto> GetDailyReportAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default);
    Task<DailySaleItemsResponseDto> GetDailySaleItemsAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default);
    Task<PeriodReportDto> GetPeriodReportAsync(PeriodReportRequest request, bool canViewProfit = false, CancellationToken cancellationToken = default);
    Task<ComprehensiveReportDto> GetComprehensiveReportAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default);
}
