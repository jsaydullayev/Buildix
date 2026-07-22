using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>Customers Excel export, moved verbatim out of CustomersController.</summary>
public sealed class CustomersExcelExportService(
    ICustomerService customerService,
    IExcelService excelService,
    ITashkentClock clock) : ICustomersExcelExportService
{
    public async Task<ExcelExportResult> ExportCustomersAsync(string lang, CancellationToken cancellationToken = default)
    {
        var customers = await customerService.GetAllCustomersAsync(cancellationToken);
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);

        object exportData = isRu
            ? customers.Select(c => new
            {
                ID = c.Id.ToString(),
                ФИО = c.FullName ?? "",
                Телефон = c.Phone,
                Общий_долг = c.TotalDebt,
                Примечание = c.Comment ?? ""
            }).Cast<object>()
            : customers.Select(c => new
            {
                ID = c.Id.ToString(),
                Ism = c.FullName ?? "",
                Telefon = c.Phone,
                Jami_qarz = c.TotalDebt,
                Izoh = c.Comment ?? ""
            }).Cast<object>();

        var sheetName = isRu ? "Клиенты" : "Mijozlar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
