using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Sales-list Excel export, moved verbatim out of SalesController so the
/// controller stays thin. Bilingual (uz/ru) column headers, role-masked cost
/// and profit columns (Owner/Admin see them, others get a dash), timestamped
/// filename from the Tashkent clock.
/// </summary>
public sealed class SalesExcelExportService(
    ISaleQueryService saleQueryService,
    IExcelService excelService,
    ITashkentClock clock) : ISalesExcelExportService
{
    public async Task<ExcelExportResult> ExportSalesAsync(string lang, bool canViewCost, bool canViewProfit, CancellationToken cancellationToken = default)
    {
        var sales = await saleQueryService.GetAllSalesAsync(cancellationToken);
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        var orderedSales = sales.OrderByDescending(s => s.CreatedAt);
        // Both columns are permission-gated by the controller: cost via
        // data.costPrice (canViewCost), profit via data.profit (canViewProfit).

        object exportData = isRu
            ? orderedSales.SelectMany(sale => sale.Items.Select(item => new
            {
                Дата = sale.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Клиент = sale.CustomerName ?? "—",
                Продавец = sale.SellerName,
                Статус = sale.Status,
                Товар = item.ProductName,
                Количество = FormatDecimal(item.Quantity),
                Ед_изм = item.Unit,
                Цена_закупки = canViewCost ? FormatDecimal(item.CostPrice) : "—",
                Цена_продажи = FormatDecimal(item.SalePrice),
                Сумма = FormatDecimal(item.TotalPrice),
                Прибыль = canViewProfit ? FormatDecimal(item.Profit) : "—"
            })).Cast<object>()
            : orderedSales.SelectMany(sale => sale.Items.Select(item => new
            {
                Sana = sale.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                Mijoz = sale.CustomerName ?? "Mijoz yo'q",
                Sotuvchi = sale.SellerName,
                Holat = sale.Status,
                Tovar_nomi = item.ProductName,
                Miqdor = FormatDecimal(item.Quantity),
                Birlik = item.Unit,
                Harid_narxi = canViewCost ? FormatDecimal(item.CostPrice) : "—",
                Sotish_narxi = FormatDecimal(item.SalePrice),
                Jami_summa = FormatDecimal(item.TotalPrice),
                Foyda = canViewProfit ? FormatDecimal(item.Profit) : "—"
            })).Cast<object>();

        var sheetName = isRu ? "Продажи" : "Sotuvlar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";

        return new ExcelExportResult(fileContent, fileName);
    }

    /// <summary>Decimal — butun sonlarda ".00" ni olib tashlaydi.</summary>
    private static string FormatDecimal(decimal value) => value.ToString("0.##");
}
