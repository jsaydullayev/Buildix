using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Builds the customers Excel workbook (bilingual headers) — bytes + filename.</summary>
public interface ICustomersExcelExportService
{
    Task<ExcelExportResult> ExportCustomersAsync(string lang, CancellationToken cancellationToken = default);
}
