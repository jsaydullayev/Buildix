using System.Globalization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>Debts Excel export — one row per debtor (matches the Долги screen).</summary>
public sealed class DebtsExcelExportService(
    IDebtQueryService debtQueryService,
    IExcelService excelService,
    ITashkentClock clock) : IDebtsExcelExportService
{
    public async Task<ExcelExportResult> ExportDebtsAsync(string lang, CancellationToken cancellationToken = default)
    {
        var debtors = await debtQueryService.GetDebtorSummariesAsync(null, null, cancellationToken);
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);

        static string Due(DateTime? d) =>
            d.HasValue ? d.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "—";

        object exportData = isRu
            ? debtors.Select(d => new
            {
                ФИО = d.CustomerName ?? "",
                Телефон = d.CustomerPhone,
                Тип = d.CustomerType == "Legal" ? "Юр. лицо" : "Физ. лицо",
                Долг = d.RemainingDebt,
                Кол_во = d.DebtCount,
                Срок = Due(d.NearestDueDate),
                Статус = d.IsOverdue ? "Просрочен" : "—"
            }).Cast<object>()
            : debtors.Select(d => new
            {
                Ism = d.CustomerName ?? "",
                Telefon = d.CustomerPhone,
                Turi = d.CustomerType == "Legal" ? "Yuridik" : "Jismoniy",
                Qarz = d.RemainingDebt,
                Soni = d.DebtCount,
                Muddat = Due(d.NearestDueDate),
                Holat = d.IsOverdue ? "Muddati o'tgan" : "—"
            }).Cast<object>();

        var sheetName = isRu ? "Долги" : "Qarzlar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
