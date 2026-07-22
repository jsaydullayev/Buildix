using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>Products Excel export, moved verbatim out of ProductsController.</summary>
public sealed class ProductsExcelExportService(
    IProductQueryService productQueryService,
    IExcelService excelService,
    ITashkentClock clock) : IProductsExcelExportService
{
    public async Task<ExcelExportResult> ExportProductsAsync(string lang, bool canViewCost, CancellationToken cancellationToken = default)
    {
        var products = await productQueryService.GetAllProductsAsync();
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);

        object exportData = isRu
            ? products.Select(p => new
            {
                ID = p.Id.ToString(),
                Название = p.Name,
                Категория = p.CategoryName ?? "",
                Цена_закупки = canViewCost ? p.CostPrice.ToString("G29") : "—",
                Цена_продажи = p.SalePrice,
                Минимальная_цена = p.MinSalePrice,
                Количество = p.Quantity,
                Ед_изм = p.UnitName,
                Минимальный_остаток = p.MinThreshold,
                Заканчивается = p.IsLowStock ? "Да" : "Нет",
                Временный = p.IsTemporary ? "Да" : "Нет"
            }).Cast<object>()
            : products.Select(p => new
            {
                ID = p.Id.ToString(),
                Nomi = p.Name,
                Kategoriya = p.CategoryName ?? "",
                Xarid_narxi = canViewCost ? p.CostPrice.ToString("G29") : "—",
                Sotuv_narxi = p.SalePrice,
                Minimal_narx = p.MinSalePrice,
                Miqdor = p.Quantity,
                Birlik = p.UnitName,
                Minimal_chegara = p.MinThreshold,
                Kam_qoldi = p.IsLowStock ? "Ha" : "Yo'q",
                Vaqtinchalik = p.IsTemporary ? "Ha" : "Yo'q"
            }).Cast<object>();

        var sheetName = isRu ? "Товары" : "Mahsulotlar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
