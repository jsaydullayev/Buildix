using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Constants;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.API.Services;
using Buildix.Domain.Constants;
using Buildix.Domain.Interfaces;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class ProductsController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IProductService _productService;
    private readonly IProductQueryService _productQueryService;
    private readonly IProductImageService _productImageService;
    private readonly IProductsExcelExportService _productsExcelExportService;
    private readonly IProductImportService _importService;
    private readonly IImageIngestService _imageIngest;
    private readonly IProductLabelService _labelService;

    public ProductsController(
        IProductService productService,
        IProductQueryService productQueryService,
        IProductImageService productImageService,
        IProductsExcelExportService productsExcelExportService,
        IProductImportService importService,
        IImageIngestService imageIngest,
        IProductLabelService labelService)
    {
        _productService = productService;
        _productQueryService = productQueryService;
        _productImageService = productImageService;
        _productsExcelExportService = productsExcelExportService;
        _importService = importService;
        _imageIngest = imageIngest;
        _labelService = labelService;
    }

    /// <summary>Cost-price visibility is gated by the <c>data.costPrice</c>
    /// permission (not a hardcoded role): Owner/SuperAdmin always; an Admin only
    /// while the Owner grants it (a customized Admin can have it revoked); a Seller
    /// never (SellerForbidden hard-strips it). The bool flows to ProductService,
    /// which masks CostPrice → 0 when false.</summary>
    private bool CanViewCost() => HasPermission(PermissionKeys.DataCostPrice);

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<ProductDto>> GetProduct(Guid id, CancellationToken ct = default)
    {
        var product = await _productQueryService.GetProductByIdAsync(id, CanViewCost(), ct);
        if (product is null)
            return NotFound();

        return Ok(product);
    }

    /// <summary>
    /// Tovarga ichki EAN-13 kod biriktiradi (kodi yo'q bo'lsa). Kod allaqachon
    /// bo'lsa o'sha qaytariladi — <c>?replace=true</c> bilangina almashtiriladi.
    /// </summary>
    /// <remarks>
    /// Almashtirish ataylab qiyinlashtirilgan: kod o'zgarsa, allaqachon chop
    /// etilib tovarlarga yopishtirilgan yorliqlar ishlamay qoladi.
    /// </remarks>
    /// <summary>
    /// Bo'sh ichki EAN-13 kod taklif qiladi — hech narsa saqlamaydi. Tovar
    /// formasidagi «Yaratish» tugmasi shuni chaqiradi; kod bazaga forma
    /// saqlanganda yoziladi.
    /// </summary>
    [HttpGet("~/api/Products/barcode/suggest")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<object>> SuggestBarcode(CancellationToken ct = default)
    {
        var result = await _labelService.SuggestBarcodeAsync(ct);
        return result.IsSuccess ? Ok(new { barcode = result.Value }) : ToActionResult(result);
    }

    [HttpPost("~/api/Products/{id:guid}/barcode")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<object>> GenerateBarcode(
        Guid id, [FromQuery] bool replace = false, CancellationToken ct = default)
    {
        var result = await _labelService.GenerateBarcodeAsync(id, replace, ct);
        return result.IsSuccess ? Ok(new { barcode = result.Value }) : ToActionResult(result);
    }

    /// <summary>
    /// Tanlangan tovarlar uchun yorliq PDF i (har nusxa — alohida sahifa).
    /// Kodsiz tovarlarga kod shu yerda avtomatik beriladi.
    /// </summary>
    [HttpPost("~/api/Products/labels")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<IActionResult> PrintLabels([FromBody] PrintLabelsDto request, CancellationToken ct = default)
    {
        var result = await _labelService.RenderLabelsAsync(request, ct);
        if (!result.IsSuccess) return ToActionResult(result).Result!;

        // inline — brauzer PDF ni yangi oynada ochadi va foydalanuvchi bevosita
        // chop etadi; yuklab olingan fayl ortiqcha qadam bo'lardi.
        return File(result.Value, "application/pdf", $"labels_{DateTime.UtcNow:yyyy-MM-dd_HHmm}.pdf");
    }

    /// <summary>
    /// Skaner uchun: shtrix-kod bo'yicha aniq moslik. Topilmasa 404.
    /// </summary>
    /// <remarks>
    /// Ruxsat — <c>products.access</c>, katalogni ko'rish bilan bir xil: skaner
    /// kassirga allaqachon ochiq bo'lgan ma'lumotni tezroq topib beradi, xolos.
    /// Narx cheklovlari ham o'zgarmaydi — CanViewCost() odatdagidek qo'llanadi,
    /// ya'ni kassir tannarxni bu yo'l bilan ham ko'ra olmaydi.
    /// </remarks>
    // Absolute route ("~/") — controller'dagi [action] konvensiyasini chetlab
    // o'tadi, aks holda yo'l /api/Products/GetProductByBarcode/by-barcode/{kod}
    // bo'lib ketardi. Xuddi /summary dagi kabi.
    [HttpGet("~/api/Products/by-barcode/{barcode}")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<ProductDto>> GetProductByBarcode(string barcode, CancellationToken ct = default)
    {
        var product = await _productQueryService.GetProductByBarcodeAsync(barcode, CanViewCost(), ct);
        if (product is null)
            return NotFound();

        return Ok(product);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetAllProducts(CancellationToken ct = default)
    {
        var products = await _productQueryService.GetAllProductsAsync(CanViewCost(), ct);
        // X-Total-Count: -1 → mijoz ma'lumotlar to'liq emasligini biladi
        var list = products.ToList();
        if (list.Count >= 5000)
            Response.Headers["X-Total-Count"] = "-1";
        return Ok(list);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAllProductsPaged(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string? search = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool lowStockOnly = false,
        [FromQuery] bool includeHidden = false,
        CancellationToken ct = default)
    {
        // includeHidden — faqat admin Товары/Склад ekranlari yuboradi; POS va
        // sotuvchi katalogi standart (false) ishlatadi, shuning uchun yashirilgan
        // tovarlar kassada ko'rinmaydi.
        var result = await _productQueryService.GetAllProductsPagedAsync(
            page, size, CanViewCost(), search, categoryId, lowStockOnly, includeHidden, ct);
        return Ok(result);
    }

    [HttpGet("low-stock")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetLowStockProducts(CancellationToken ct = default)
    {
        var products = await _productQueryService.GetLowStockProductsAsync(CanViewCost(), ct);
        return Ok(products);
    }

    /// <summary>Warehouse KPI tiles (positions / stock value / low / out),
    /// aggregated DB-side — replaces the client-side rollup over the full list.</summary>
    // Absolute route — mijoz doim /api/Products/summary chaqiradi; [action]
    // konvensiyasi bilan bu /GetWarehouseSummary/summary bo'lib 404 berardi.
    [HttpGet("~/api/Products/summary")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<WarehouseSummaryDto>> GetWarehouseSummary(CancellationToken ct = default)
        => Ok(await _productQueryService.GetWarehouseSummaryAsync(CanViewCost(), ct));

    /// <summary>
    /// Barcha o'lchov birliklarini olish (dona, kg, m)
    /// </summary>
    [HttpGet("units")]
    [AllowAnonymous]
    public ActionResult<List<UnitInfo>> GetUnits()
    {
        return Ok(UnitConstants.AllUnits);
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.ProductsCreate)]
    public async Task<ActionResult<ProductDto>> CreateProduct([FromBody] CreateProductDto request, CancellationToken ct = default)
    {
        var sellerId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var sellerGuid = !string.IsNullOrEmpty(sellerId) && Guid.TryParse(sellerId, out var parsed)
            ? parsed : (Guid?)null;

        var result = await _productService.CreateProductAsync(request, sellerGuid);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(GetProduct), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id}")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, [FromBody] UpdateProductDto request, CancellationToken ct = default)
    {
        if (id != request.Id)
            return BadRequest("ID mismatch");

        // On-hand stock may only be hand-corrected by Owner/SuperAdmin. For any
        // other caller (even an Admin with products.edit) request.Quantity is
        // ignored server-side; stock otherwise moves only through zakup/sales.
        var canEditStock = User.FindFirst(ClaimTypes.Role)?.Value is "Owner" or "SuperAdmin";
        // Kelgan (tannarx) narxini forma orqali faqat cost-ko'ruvchi (Owner/Admin)
        // o'zgartira oladi — masking tufayli boshqa rol 0 yuborib eski narxni
        // yo'qotib qo'ymasin.
        var canEditCost = CanViewCost();

        // The service records the stock-adjust audit (old→new) itself when an
        // Owner/SuperAdmin actually moves the on-hand figure.
        var result = await _productService.UpdateProductAsync(request, CurrentUserId(), canEditStock, canEditCost, ct);
        if (result.IsFailure)
            return result.Code == NotFoundCode ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(result.Value);
    }

    /// <summary>
    /// Bitta tovarning ombor harakatlari — Склад "Движение товара" oynasi
    /// (Приход/Продажа/Корректировка + har birida qoldiq holati).
    /// </summary>
    // Absolute route ("~/") — [action] konvensiyasini chetlab, toza
    // /api/Products/{id}/movements beradi (dizayndagidek).
    [HttpGet("~/api/Products/{id}/movements")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<IReadOnlyList<StockMovementDto>>> GetProductMovements(
        Guid id, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await _productQueryService.GetProductMovementsAsync(id, limit, ct));

    /// <summary>Seller tovar kartochkasi statistikasi (supplier/oxirgi приход/oyiga sotilgan).</summary>
    [HttpGet("~/api/Products/{id}/stats")]
    [RequirePermission(PermissionKeys.ProductsAccess)]
    public async Task<ActionResult<ProductStatsDto>> GetProductStats(Guid id, CancellationToken ct = default)
    {
        var stats = await _productQueryService.GetProductStatsAsync(id, ct);
        return stats is null ? NotFound() : Ok(stats);
    }

    /// <summary>
    /// Товары/Склад ekranidagi inline tahrir — sotuv narxi / min. qoldiq /
    /// ko'rinish (Скрыть). Faqat berilgan maydon(lar)ni o'zgartiradi va
    /// auditlaydi. Qoldiq/tannarx bu yerdan o'zgarmaydi (ular alohida yo'llar).
    /// </summary>
    [HttpPatch("{id}")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<ProductDto>> PatchProduct(Guid id, [FromBody] ProductPatchDto request, CancellationToken ct = default)
    {
        var result = await _productService.PatchProductAsync(id, request, CurrentUserId(), ct);
        if (result.IsFailure)
            return result.Code == NotFoundCode ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(result.Value);
    }

    /// <summary>
    /// Инвентаризация — bulk physical stock count. Sets each product's on-hand
    /// figure to the counted value and journals the variance. Fraud-sensitive:
    /// Owner/SuperAdmin only (same gate as manual stock correction).
    /// </summary>
    [HttpPost("~/api/Products/stocktake")]
    [Authorize(Roles = "Owner,SuperAdmin")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<StocktakeResultDto>> Stocktake([FromBody] StocktakeRequest request, CancellationToken ct = default)
    {
        var result = await _productService.StocktakeAsync(request, CurrentUserId(), ct);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.ProductsDelete)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct = default)
    {
        var result = await _productService.DeleteProductAsync(id);
        if (!result)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Exports all products as an .xlsx workbook. Column headers (and the
    /// "Ha"/"Yo'q" boolean labels) come back in the caller's language —
    /// pass `lang=ru` for Russian, anything else (or omit) yields Uzbek.
    /// Returns every product the caller can see — same paging-disabled
    /// fetch used by the in-app list. No filtering server-side; the user
    /// can sort / filter in Excel if needed.
    /// </summary>
    [HttpGet("export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ProductsExport)]
    public async Task<IActionResult> ExportProductsToExcel(
        [FromQuery] string lang = "uz",
        CancellationToken ct = default)
    {
        var result = await _productsExcelExportService.ExportProductsAsync(lang, CanViewCost(), cancellationToken: ct);
        return File(result.Content, XlsxContentType, result.FileName);
    }

    /// <summary>
    /// Mahsulotga rasm biriktiradi yoki mavjudini almashtiradi. Ikki formatni
    /// qabul qiladi: multipart/form-data (`image` fayl) yoki JSON
    /// ({ "image": "data:image/...;base64,..." }). Faqat savdo ekranida
    /// ko'rsatish uchun. Magic-byte va hajm (max 5MB) tekshiruvidan o'tadi.
    /// </summary>
    [HttpPost("{id}/image")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    // 5MB raw rasm base64'da ~6.7MB bo'ladi; 8MB ceiling boundary'da 413 emas,
    // aniq "5MB" xabarini beruvchi ichki tekshiruvga yetib borishni kafolatlaydi.
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<ProductDto>> SetImage(Guid id, CancellationToken ct = default)
    {
        // Multipart yoki JSON-base64 rasmni parse + validatsiya qilish (magic-byte,
        // 5MB) endi IImageIngestService ichida — controller faqat natijani oladi.
        var ingest = await _imageIngest.ReadProductImageAsync(Request, ct);
        if (ingest.IsFailure)
            return BadRequest(ingest.Error);

        var product = await _productImageService.SetProductImageAsync(id, ingest.Value.Bytes, ingest.Value.Extension, CurrentUserId(), ct);
        if (product is null)
            return NotFound();

        return Ok(product);
    }

    /// <summary>Mahsulot rasmini o'chiradi.</summary>
    [HttpDelete("{id}/image")]
    [RequirePermission(PermissionKeys.ProductsEdit)]
    public async Task<ActionResult<ProductDto>> RemoveImage(Guid id, CancellationToken ct = default)
    {
        var product = await _productImageService.RemoveProductImageAsync(id, CurrentUserId(), ct);
        if (product is null)
            return NotFound();

        return Ok(product);
    }

    /// <summary>The authenticated caller's user id from the JWT (Guid.Empty if absent).</summary>
    private Guid CurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Dry-run: Excel qatorlarini tahlil qiladi, bazaga yozmaydi.
    /// Flutter bu natijani foydalanuvchiga ko'rsatadi (xatolar, ogohlantirishlar, kategoriya takliflari).
    /// </summary>
    [HttpPost("import/preview")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ProductsImport)]
    public async Task<ActionResult<ImportPreviewResultDto>> ImportPreview(
        [FromBody] List<ImportProductRowDto> rows,
        CancellationToken ct = default)
    {
        if (rows is null or { Count: 0 })
            return BadRequest(new { message = "Kamida bitta qator yuborilishi kerak." });

        if (rows.Count > 1000)
            return BadRequest(new { message = "Bir marta maksimum 1000 ta qator import qilish mumkin." });

        var result = await _importService.PreviewAsync(rows, ct);
        return Ok(result);
    }

    /// <summary>
    /// Haqiqiy import: faqat Error bo'lmagan qatorlarni saqlaydi.
    /// CategoryOverrides — foydalanuvchi kategoriya moslamalarini tasdiqlaydi.
    /// </summary>
    [HttpPost("import/confirm")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ProductsImport)]
    public async Task<ActionResult<ImportResultDto>> ImportConfirm(
        [FromBody] ImportConfirmDto request,
        CancellationToken ct = default)
    {
        if (request.Rows is null or { Count: 0 })
            return BadRequest(new { message = "Kamida bitta qator yuborilishi kerak." });

        if (request.Rows.Count > 1000)
            return BadRequest(new { message = "Bir marta maksimum 1000 ta qator import qilish mumkin." });

        var result = await _importService.ConfirmAsync(request, ct);
        return Ok(result);
    }

}
