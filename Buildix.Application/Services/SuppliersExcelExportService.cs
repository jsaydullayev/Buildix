using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Suppliers Excel export, moved verbatim out of SuppliersController. The
/// confidential outstanding-debt figure is zeroed for Seller callers (the same
/// redaction the read endpoints apply).
/// </summary>
public sealed class SuppliersExcelExportService(
    ISupplierService supplierService,
    IExcelService excelService,
    ITashkentClock clock) : ISuppliersExcelExportService
{
    public async Task<ExcelExportResult> ExportSuppliersAsync(string lang, string? userRole, CancellationToken cancellationToken = default)
    {
        // GetAllSuppliersAsync redacts the confidential debt for Seller callers.
        var suppliers = await supplierService.GetAllSuppliersAsync(userRole, cancellationToken);
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);

        object exportData = isRu
            ? suppliers.Select(s => new
            {
                ID = s.Id.ToString(),
                Название = s.Name,
                Телефон = s.Phone ?? "",
                Адрес = s.Address ?? "",
                Долг = s.OutstandingDebt,
                Примечание = s.Comment ?? ""
            }).Cast<object>()
            : suppliers.Select(s => new
            {
                ID = s.Id.ToString(),
                Nomi = s.Name,
                Telefon = s.Phone ?? "",
                Manzil = s.Address ?? "",
                Qarz = s.OutstandingDebt,
                Izoh = s.Comment ?? ""
            }).Cast<object>();

        var sheetName = isRu ? "Поставщики" : "Yetkazib beruvchilar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
