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
    /// <param name="lowStockOnly">true → only products at or below their minimum
    /// (the Telegram bot's «kam qolgan mahsulotlar»); false exports the catalogue.</param>
    Task<ExcelExportResult> ExportProductsAsync(string lang, bool canViewCost,
        bool lowStockOnly = false, CancellationToken cancellationToken = default);
}
