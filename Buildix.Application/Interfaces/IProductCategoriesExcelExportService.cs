using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Builds the product-categories Excel workbook (bilingual headers + status) — bytes + filename.</summary>
public interface IProductCategoriesExcelExportService
{
    Task<ExcelExportResult> ExportCategoriesAsync(string lang, CancellationToken cancellationToken = default);
}
