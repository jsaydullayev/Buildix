using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the zakups (procurement) Excel workbook — cost price and total masked
/// (rendered "-") when <c>canViewCost</c> is false (the controller passes the
/// data.costPrice permission result). Returns bytes + filename.
/// </summary>
public interface IZakupsExcelExportService
{
    Task<ExcelExportResult> ExportZakupsAsync(bool canViewCost, CancellationToken cancellationToken = default);
}
