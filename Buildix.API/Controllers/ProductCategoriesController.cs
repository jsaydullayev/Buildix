using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.Domain.Constants;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class ProductCategoriesController : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IProductCategoryService _categoryService;
    private readonly IProductCategoriesExcelExportService _categoriesExcelExportService;

    public ProductCategoriesController(IProductCategoryService categoryService, IProductCategoriesExcelExportService categoriesExcelExportService)
    {
        _categoryService = categoryService;
        _categoriesExcelExportService = categoriesExcelExportService;
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.CategoriesAccess)]
    public async Task<ActionResult<IEnumerable<ProductCategoryDto>>> GetAllCategories(CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetAllCategoriesAsync(cancellationToken);
        return Ok(categories);
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.CategoriesAccess)]
    public async Task<ActionResult<ProductCategoryDto>> GetCategoryById(int id, CancellationToken cancellationToken)
    {
        var category = await _categoryService.GetCategoryByIdAsync(id, cancellationToken);
        if (category is null)
            return NotFound();

        return Ok(category);
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.CategoriesManage)]
    public async Task<ActionResult<ProductCategoryDto>> CreateCategory(
        [FromBody] CreateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.CreateCategoryAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetCategoryById), new { id = category.Id }, category);
    }

    [HttpPut]
    [RequirePermission(PermissionKeys.CategoriesManage)]
    public async Task<ActionResult<ProductCategoryDto>> UpdateCategory(
        [FromBody] UpdateProductCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.UpdateCategoryAsync(request, cancellationToken);
        if (category is null)
            return NotFound();

        return Ok(category);
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.CategoriesManage)]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken)
    {
        var success = await _categoryService.DeleteCategoryAsync(id, cancellationToken);
        if (!success)
            return NotFound();

        return Ok(new { message = "Category muvaffaqiyatli o'chirildi" });
    }

    /// <summary>
    /// Exports categories as a real .xlsx workbook (previously emitted CSV
    /// despite the "ToExcel" name). Column headers come back in the caller's
    /// language — pass `lang=ru` for Russian, anything else yields Uzbek.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionKeys.CategoriesAccess)]
    public async Task<IActionResult> ExportCategoriesToExcel(
        [FromQuery] string lang = "uz",
        CancellationToken cancellationToken = default)
    {
        var result = await _categoriesExcelExportService.ExportCategoriesAsync(lang, cancellationToken);
        return File(result.Content, XlsxContentType, result.FileName);
    }
}
