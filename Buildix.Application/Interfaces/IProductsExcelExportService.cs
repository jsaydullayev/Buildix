using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the products Excel workbook (bilingual headers, cost column masked "—"
/// when <c>canViewCost</c> is false — the controller passes the data.costPrice
/// permission result). Returns the raw bytes + filename; the controller only
/// wraps the result in File(...).
/// </summary>
public interface IProductsExcelExportService
{
    Task<ExcelExportResult> ExportProductsAsync(string lang, bool canViewCost, CancellationToken cancellationToken = default);
}
