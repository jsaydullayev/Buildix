using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Tovarlar ro'yxatining Excel eksporti.
/// </summary>
/// <remarks>
/// <para>Ustunlar ILOVA TILIDA chiqadi — inglizcha ham. Ilgari faqat ikki til
/// bor edi va inglizcha jimgina o'zbekchaga tushardi.</para>
///
/// <para><b>ID ustuni olib tashlandi.</b> U ichki identifikator (GUID) —
/// o'ttiz olti belgilik satr, hech kim uni o'qimaydi va u faylni faqat
/// kengaytirardi. Uning o'rniga ARTIKUL turadi: tovarni odam ham, ombor ham
/// aynan shu bilan taniydi.</para>
///
/// <para><b>«Kam qoldi» va «Vaqtinchalik» ustunlari olib tashlandi.</b>
/// Birinchisi qoldiq va eng kam chegaradan o'z-o'zidan kelib chiqadi —
/// ikkalasi ham yonma-yon turibdi, uchinchi ustunda takrorlashning ma'nosi
/// yo'q. Ikkinchisi kassada shosha-pisha yaratilgan qatorni bildiradigan
/// ichki bayroq va ombor ro'yxatida hech narsani hal qilmaydi.</para>
/// </remarks>
public sealed class ProductsExcelExportService(
    IProductQueryService productQueryService,
    IExcelService excelService,
    ITashkentClock clock) : IProductsExcelExportService
{
    private static readonly Localized SheetName = new("Mahsulotlar", "Товары", "Products");

    private static readonly Localized[] Headers =
    [
        new("Artikul", "Артикул", "SKU"),
        new("Nomi", "Название", "Name"),
        new("Kategoriya", "Категория", "Category"),
        new("Xarid narxi", "Цена закупки", "Cost price"),
        new("Sotuv narxi", "Цена продажи", "Sale price"),
        new("Minimal narx", "Минимальная цена", "Minimum price"),
        new("Miqdor", "Количество", "Quantity"),
        new("Birlik", "Ед. изм.", "Unit"),
        new("Minimal chegara", "Минимальный остаток", "Minimum stock"),
    ];

    public async Task<ExcelExportResult> ExportProductsAsync(string lang, bool canViewCost,
        bool lowStockOnly = false, CancellationToken cancellationToken = default)
    {
        var products = await productQueryService.GetAllProductsAsync();
        if (lowStockOnly)
            products = products.Where(p => p.IsLowStock || p.Quantity <= 0).ToList();

        var rows = products.Select(p => (IReadOnlyList<object?>)new object?[]
        {
            p.Sku ?? "",
            p.Name,
            p.CategoryName ?? "",
            canViewCost ? p.CostPrice.ToString("G29") : "—",
            p.SalePrice,
            p.MinSalePrice,
            p.Quantity,
            p.UnitName,
            p.MinThreshold,
        });

        var sheetName = SheetName.For(lang);
        var headers = Headers.Select(h => h.For(lang)).ToList();
        var fileContent = excelService.GenerateExcel(headers, rows, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
