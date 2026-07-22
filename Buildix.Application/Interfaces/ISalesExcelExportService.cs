using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the sales-list Excel workbook (bilingual). The cost and profit columns
/// are each masked "—" when their permission flag is false — the controller passes
/// <c>canViewCost</c> (data.costPrice) and <c>canViewProfit</c> (data.profit). The
/// service never touches HttpContext; the controller only wraps it in File(...).
/// </summary>
public interface ISalesExcelExportService
{
    Task<ExcelExportResult> ExportSalesAsync(string lang, bool canViewCost, bool canViewProfit, CancellationToken cancellationToken = default);
}
