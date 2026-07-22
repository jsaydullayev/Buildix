using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the suppliers Excel workbook (bilingual headers) — the confidential
/// outstanding-debt figure is zeroed for Seller callers. Returns bytes + filename.
/// </summary>
public interface ISuppliersExcelExportService
{
    Task<ExcelExportResult> ExportSuppliersAsync(string lang, string? userRole, CancellationToken cancellationToken = default);
}
