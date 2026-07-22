using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// Dashboard aggregations — read-only rollups over Sales / SaleItems / Users /
/// Shifts that back the dashboard widgets (owner summary counters, weekly chart,
/// top products, staff leaderboard, and a seller's own metrics).
/// Split out of the former monolithic <c>IReportService</c> so each concern is a
/// small, focused service.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Pre-aggregated Owner-dashboard counters (customers, low-stock, pending /
    /// overdue debts) computed server-side so the client no longer downloads
    /// and folds three full catalogs on the UI isolate.
    /// </summary>
    Task<DashboardSummaryDto> GetOwnerDashboardSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Last N Tashkent days of revenue/profit/check counts as a time series.
    /// Profit is zero unless the caller is Owner. Optionally compares against the
    /// previous equally-sized window.
    /// </summary>
    Task<WeeklySeriesDto> GetWeeklySeriesAsync(int days, bool compare = false, bool canViewProfit = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Top-N products in the period, ranked by quantity / revenue / profit.
    /// Profit is hidden for non-Owner callers.
    /// </summary>
    Task<TopProductsDto> GetTopProductsAsync(string period, string sortBy, int limit, bool canViewProfit = false, CancellationToken cancellationToken = default);

    /// <summary>Per-staff sales metrics for the period (includes zero-sales staff).</summary>
    Task<StaffPerformanceDto> GetStaffPerformanceAsync(string period, CancellationToken cancellationToken = default);

    /// <summary>One seller's own metrics for the Seller dashboard.</summary>
    Task<MyPerformanceDto> GetMyPerformanceAsync(Guid userId, string period, CancellationToken cancellationToken = default);
}
