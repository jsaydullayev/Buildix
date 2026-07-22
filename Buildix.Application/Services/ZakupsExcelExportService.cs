using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>Zakups (procurement) Excel export, moved verbatim out of ZakupsController.</summary>
public sealed class ZakupsExcelExportService(
    IZakupService zakupService,
    IExcelService excelService,
    ITashkentClock clock) : IZakupsExcelExportService
{
    public async Task<ExcelExportResult> ExportZakupsAsync(bool canViewCost, CancellationToken cancellationToken = default)
    {
        var zakups = await zakupService.GetAllZakupsAsync();

        // Hide cost price + total for callers without data.costPrice.
        var exportData = zakups.Select(z => new
        {
            ID = z.Id.ToString(),
            Mahsulot = z.ProductName,
            Sana = z.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            Xodim = z.CreatedBy,
            Miqdor = z.Quantity,
            Xarid_narxi = canViewCost ? z.CostPrice.ToString() : "-",
            Jami_summa = canViewCost ? (z.Quantity * z.CostPrice).ToString() : "-"
        });

        var fileContent = excelService.GenerateExcel(exportData, "Xaridlar");
        var fileName = $"Xaridlar_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}