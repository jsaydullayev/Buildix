using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// Sales-list read models — role-filtered daily/range sales list, monthly
/// category breakdown, and detailed sales-with-items for export.
/// </summary>
public interface ISalesListService
{
    /// <summary>
    /// Sales list for a single Tashkent day, or — when <paramref name="endDate"/>
    /// is supplied — for the inclusive [date, endDate] day range.
    /// </summary>
    Task<DailySalesListDto> GetDailySalesListAsync(DateTime date, string? userRole = null, bool canViewProfit = false, Guid? userId = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<MonthlyCategorySalesResponseDto> GetMonthlyCategorySalesAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default);
}
