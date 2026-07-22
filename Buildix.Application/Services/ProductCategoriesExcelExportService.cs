using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>Product-categories Excel export, moved verbatim out of ProductCategoriesController.</summary>
public sealed class ProductCategoriesExcelExportService(
    IProductCategoryService categoryService,
    IExcelService excelService,
    ITashkentClock clock) : IProductCategoriesExcelExportService
{
    public async Task<ExcelExportResult> ExportCategoriesAsync(string lang, CancellationToken cancellationToken = default)
    {
        var categories = await categoryService.GetAllCategoriesAsync(cancellationToken);
        var isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);

        object exportData = isRu
            ? categories.Select(c => new
            {
                ID = c.Id.ToString(),
                Название = c.Name,
                Описание = c.Description ?? "",
                Значок = c.Icon ?? "",
                Статус = c.IsActive ? "Активна" : "Неактивна",
                Количество_товаров = c.ProductCount
            }).Cast<object>()
            : categories.Select(c => new
            {
                ID = c.Id.ToString(),
                Nomi = c.Name,
                Tavsifi = c.Description ?? "",
                Belgi = c.Icon ?? "",
                Holati = c.IsActive ? "Faol" : "Nofaol",
                Mahsulotlar_soni = c.ProductCount
            }).Cast<object>();

        var sheetName = isRu ? "Категории" : "Kategoriyalar";
        var fileContent = excelService.GenerateExcel((dynamic)exportData, sheetName);
        var fileName = $"{sheetName}_{clock.NowLocal:yyyyMMdd_HHmmss}.xlsx";
        return new ExcelExportResult(fileContent, fileName);
    }
}
