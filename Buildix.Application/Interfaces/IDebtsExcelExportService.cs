using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Builds the debts (debtor summary) Excel workbook — bytes + filename.</summary>
public interface IDebtsExcelExportService
{
    Task<ExcelExportResult> ExportDebtsAsync(string lang, CancellationToken cancellationToken = default);
}
