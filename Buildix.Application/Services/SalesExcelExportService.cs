using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Sotuvlar ro'yxatining Excel eksporti.
/// </summary>
/// <remarks>
/// <para>Ustun nomlari va qiymatlari ILOVA TILIDA chiqadi. Ilgari faqat
/// o'zbekcha va ruscha variant bor edi, inglizcha esa jimgina o'zbekchaga
/// tushardi. Holat («Paid», «Debt») esa umuman tarjima qilinmasdi — u
/// bazadagi ingliz nomi ko'rinishida, hatto ruscha faylda ham shundayligicha
/// yozilardi.</para>
///
/// <para>Tannarx va foyda ustunlari ruxsatga bog'liq: kontroller
/// <c>data.costPrice</c> va <c>data.profit</c> bo'yicha hal qiladi, ruxsat
/// bo'lmasa katakchada chiziqcha turadi.</para>
/// </remarks>
public sealed class SalesExcelExportService(
    ISaleQueryService saleQueryService,
    IExcelService excelService,
    ITashkentClock clock) : ISalesExcelExportService
{
    private static readonly Localized SheetName = new("Sotuvlar", "Продажи", "Sales");

    private static readonly Localized[] Headers =
    [
        new("Sana", "Дата", "Date"),
        // Chek raqami sanadan KEYIN turadi. Bitta chekda bir necha tovar
        // sotilishi mumkin va faylda ular alohida qator bo'lib yotadi —
        // raqamsiz ular bitta savdo ekanini ajratib bo'lmasdi.
        new("Chek", "Чек", "Receipt"),
        new("Mijoz", "Клиент", "Customer"),
        new("Sotuvchi", "Продавец", "Seller"),
        new("Holat", "Статус", "Status"),
        new("Tovar nomi", "Товар", "Product"),
        new("Miqdor", "Количество", "Quantity"),
        new("Birlik", "Ед. изм.", "Unit"),
        new("Xarid narxi", "Цена закупки", "Cost price"),
        new("Sotish narxi", "Цена продажи", "Sale price"),
        new("Jami summa", "Сумма", "Total"),
        new("Foyda", "Прибыль", "Profit"),
    ];

    private static readonly Localized NoCustomer = new("Mijoz yo'q", "—", "No customer");

    public async Task<ExcelExportResult> ExportSalesAsync(string lang, bool canViewCost, bool canViewProfit,
        DateTime? from = null, DateTime? to = null, Guid? sellerId = null,
        CancellationToken cancellationToken = default)
    {
        var sales = from.HasValue && to.HasValue
            ? await saleQueryService.GetSalesByDateRangeAsync(from.Value, to.Value, cancellationToken)
            : await saleQueryService.GetAllSalesAsync(cancellationToken);

        // A cashier without data.allSalesView gets only their own receipts.
        if (sellerId is { } seller)
            sales = sales.Where(s => s.SellerId == seller).ToList();

        var masked = "—";
        var rows = sales
            .OrderByDescending(s => s.CreatedAt)
            .SelectMany(sale => sale.Items.Select(item => (IReadOnlyList<object?>)new object?[]
            {
                sale.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                sale.SaleNumber,
                sale.CustomerName ?? NoCustomer.For(lang),
                sale.SellerName,
                SaleStatusText(sale.Status).For(lang),
                item.ProductName,
                FormatDecimal(item.Quantity),
                item.Unit,
                canViewCost ? FormatDecimal(item.CostPrice) : masked,
                FormatDecimal(item.SalePrice),
                FormatDecimal(item.TotalPrice),
                canViewProfit ? FormatDecimal(item.Profit) : masked,
            }));

        var sheetName = SheetName.For(lang);
        var headers = Headers.Select(h => h.For(lang)).ToList();
        var fileContent = excelService.GenerateExcel(headers, rows, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";

        return new ExcelExportResult(fileContent, fileName);
    }

    /// <summary>
    /// Chek holatining tarjimasi.
    ///
    /// <para>Kutilmagan qiymat bazadagi holicha qaytadi — uni yashirish
    /// «bu chek qanday holatda?» degan savolni javobsiz qoldirardi.</para>
    /// </summary>
    private static Localized SaleStatusText(string status) => status switch
    {
        "Draft" => new("Qoralama", "Черновик", "Draft"),
        "Paid" => new("To'langan", "Оплачен", "Paid"),
        "Debt" => new("Qarz", "Долг", "Debt"),
        "Closed" => new("Yopilgan", "Закрыт", "Closed"),
        "Cancelled" => new("Bekor qilingan", "Отменён", "Cancelled"),
        _ => new(status, status, status),
    };

    /// <summary>Decimal — butun sonlarda ".00" ni olib tashlaydi.</summary>
    private static string FormatDecimal(decimal value) => value.ToString("0.##");
}
