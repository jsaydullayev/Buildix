using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// Excel (ClosedXML) report exports, moved verbatim out of the controller so the
/// controller stays thin. Callers pass the already-resolved userRole/userId from
/// the request claims — the service never touches HttpContext. Each method
/// throws on failure; the controller keeps the existing try/catch → 500 shape.
/// </summary>
public interface IReportExcelExportService
{
    Task<ExcelExportResult> ExportComprehensiveReportAsync(DateTime? date, string lang, string? userRole, Guid? userId, bool canViewProfit, CancellationToken cancellationToken = default);
    Task<ExcelExportResult> ExportInventoryReportAsync(DateTime? date, string lang, string? userRole, bool canViewCost, bool canViewProfit, CancellationToken cancellationToken = default);
    Task<ExcelExportResult> ExportDailyReportAsync(DateTime date, string? userRole, Guid? userId, bool canViewProfit);
}
