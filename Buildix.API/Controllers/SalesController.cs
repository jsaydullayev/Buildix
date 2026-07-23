using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.API.Authorization;
using Buildix.API.Filters;
using Buildix.Domain.Constants;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SalesController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ISaleService _saleService;
    private readonly ISaleQueryService _saleQueryService;
    private readonly ISaleItemService _saleItemService;
    private readonly ISaleReversalService _saleReversalService;
    private readonly ISalePaymentService _salePaymentService;
    private readonly ILogger<SalesController> _logger;
    private readonly IReportPdfExportService _reportPdfExportService;
    private readonly ISalesExcelExportService _salesExcelExportService;
    private readonly ITashkentClock _clock;

    public SalesController(ISaleService saleService, ISaleQueryService saleQueryService, ISaleItemService saleItemService, ISaleReversalService saleReversalService, ISalePaymentService salePaymentService, ILogger<SalesController> logger, IReportPdfExportService reportPdfExportService, ISalesExcelExportService salesExcelExportService, ITashkentClock clock)
    {
        _saleService = saleService;
        _saleQueryService = saleQueryService;
        _saleItemService = saleItemService;
        _saleReversalService = saleReversalService;
        _salePaymentService = salePaymentService;
        _logger = logger;
        _reportPdfExportService = reportPdfExportService;
        _salesExcelExportService = salesExcelExportService;
        _clock = clock;
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<SaleDto>> GetSale(Guid id, CancellationToken ct = default)
    {
        var sale = await _saleQueryService.GetSaleByIdAsync(id);
        if (sale is null)
            return NotFound();

        return Ok(sale);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<PagedResult<SaleDto>>> GetAllSales(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string? search = null,
        [FromQuery] Guid? sellerId = null,
        [FromQuery] string? paymentType = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        // Returns a paged envelope: { items, page, size, total, totalPages }.
        // Defaults: page=1, size=50. Max size: 200 (clamped server-side).
        // Filters: search (chek №/mijoz/telefon/sotuvchi/mahsulot), sellerId,
        // paymentType (Cash|Terminal|Transfer|Click|Debt), status, from/to.
        var result = await _saleQueryService.GetSalesPagedAsync(
            page, size, search, sellerId, paymentType, status, from, to, ct);
        return Ok(result);
    }

    [HttpGet("by-date")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<IEnumerable<SaleDto>>> GetSalesByDateRange(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        CancellationToken ct = default)
    {
        if (start > end)
            return BadRequest(new { message = "Start date must be before end date" });

        // Max 90 kun — kattaroq oraliq OOM xavfi tug'diradi
        if ((end - start).TotalDays > 90)
            return BadRequest(new { message = "Sana oralig'i 90 kundan oshmasligi kerak." });

        // `start`/`end` arrive as Tashkent-local calendar days. Every other
        // dated query (reports, dashboard) anchors to Tashkent before hitting
        // the UTC-stored CreatedAt; this endpoint used to compare the raw
        // local dates directly, shifting the "day" by the GMT+5 offset. Convert
        // to a UTC half-open range [startOfFirstDay, endOfLastDay) here.
        var startUtc = _clock.LocalDayToUtcRange(start).UtcStart;
        var endUtc = _clock.LocalDayToUtcRange(end).UtcEnd;

        var sales = await _saleQueryService.GetSalesByDateRangeAsync(startUtc, endUtc, ct);
        return Ok(sales);
    }

    [HttpGet("my-drafts")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<IEnumerable<SaleDto>>> GetMyDraftSales(CancellationToken ct = default)
    {
        var sellerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sellerIdStr) || !Guid.TryParse(sellerIdStr, out var sellerId))
            return Unauthorized();

        // Kollaboratsiya: data.allSalesView ruxsati bor seller (yoki Owner/Admin)
        // butun do'kondagi draftlarni ko'rib, davom ettira oladi; aks holda
        // faqat o'zinikini.
        var sellerFilter = CanViewAllSales() ? (Guid?)null : sellerId;
        var sales = await _saleQueryService.GetDraftSalesBySellerAsync(sellerFilter);
        return Ok(sales);
    }

    [HttpGet("my-unfinished")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<IEnumerable<SaleDto>>> GetMyUnfinishedSales(CancellationToken ct = default)
    {
        var sellerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sellerIdStr) || !Guid.TryParse(sellerIdStr, out var sellerId))
            return Unauthorized();

        var sellerFilter = CanViewAllSales() ? (Guid?)null : sellerId;
        var sales = await _saleQueryService.GetUnfinishedSalesBySellerAsync(sellerFilter);
        return Ok(sales);
    }

    // Owner/Admin/SuperAdmin va data.allSalesView ruxsatiga ega sellerlar butun
    // do'kondagi sotuvlarni ko'ra/davom ettira oladi (sellerlar hamkorligi).
    // Ruxsat bo'lmasa — seller faqat o'z sotuvlarini ko'radi.
    private bool CanViewAllSales()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role is "Owner" or "SuperAdmin" or "Admin")
            return true;
        return User.HasClaim("perm", PermissionKeys.DataAllSalesView);
    }

    // Cost-price visibility is gated by data.costPrice (not a hardcoded role):
    // Owner/SuperAdmin always; Admin only while granted; Seller never.
    private bool CanViewCost() => HasPermission(PermissionKeys.DataCostPrice);

    // Profit visibility is gated by data.profit: Owner/SuperAdmin always; Admin
    // only while granted (default Admin lacks it); Seller never.
    private bool CanViewProfit() => HasPermission(PermissionKeys.DataProfit);

    [HttpPost]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleDto>> CreateSale([FromBody] CreateSaleDto request, CancellationToken ct = default)
    {
        var sellerIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sellerIdStr) || !Guid.TryParse(sellerIdStr, out var sellerId))
            return Unauthorized();

        var result = await _saleService.CreateSaleAsync(request, sellerId);
        return ToActionResult(result, sale => CreatedAtAction(nameof(GetSale), new { id = sale.Id }, sale));
    }

    [HttpPatch("{saleId}/customer")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleDto>> UpdateSaleCustomer(Guid saleId, [FromBody] UpdateSaleCustomerDto request, CancellationToken ct = default)
    {
        var result = await _saleService.UpdateSaleCustomerAsync(saleId, request);
        return ToActionResult(result);
    }

    [HttpPost("{saleId}/items")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleItemDto>> AddSaleItem(Guid saleId, [FromBody] AddSaleItemDto request, CancellationToken ct = default)
    {
        var result = await _saleItemService.AddSaleItemAsync(saleId, request);
        return ToActionResult(result);
    }

    [HttpPost("{saleId}/items/remove")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleItemDto>> RemoveSaleItem(Guid saleId, [FromBody] RemoveSaleItemDto request, CancellationToken ct = default)
    {
        _logger.LogInformation("RemoveSaleItem called - SaleId: {SaleId}, SaleItemId: {SaleItemId}, Quantity: {Quantity}",
            saleId, request.SaleItemId, request.Quantity);

        var result = await _saleItemService.RemoveSaleItemAsync(saleId, request);
        return ToActionResult(result);
    }

    [HttpPost("{saleId}/payments")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    [Idempotent("sale-payment")]
    public async Task<ActionResult<PaymentDto>> AddPayment(Guid saleId, [FromBody] AddPaymentDto request, CancellationToken ct = default)
    {
        var result = await _salePaymentService.AddPaymentAsync(saleId, request);
        return ToActionResult(result);
    }

    /// <summary>
    /// Close a sale with one or more tenders in ONE transaction — the split
    /// ("Микс") checkout. Not expressible as two /payments calls: those are not
    /// atomic, and the first partial tender is rejected on a walk-in sale
    /// (no customer ⇒ cannot leave a debt).
    /// </summary>
    [HttpPost("{saleId}/checkout")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    [Idempotent("sale-checkout")]
    public async Task<ActionResult<PaymentDto>> Checkout(Guid saleId, [FromBody] CheckoutSaleDto request, CancellationToken ct = default)
    {
        var result = await _salePaymentService.CheckoutAsync(saleId, request, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Sotuvga sale-level chegirma (skidka) qo'llash. Kassa to'lov oynasidan
    /// chaqiriladi — mahsulotlar qo'shilgach, to'lovdan oldin. Item narxlariga
    /// tegmaydi; faqat umumiy hisobni (TotalAmount) kamaytiradi, keyingi
    /// to'lovlar shu kamaytirilgan summani yopadi.
    /// </summary>
    [HttpPatch("{saleId}/discount")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleDto>> SetSaleDiscount(Guid saleId, [FromBody] SetSaleDiscountDto request, CancellationToken ct = default)
    {
        // Audit actor = authenticated caller (JWT), never a client-supplied id.
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Unauthorized();

        var result = await _saleService.SetSaleDiscountAsync(saleId, request.DiscountAmount, userId, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Savdoni o'chirish (faqat Draft va Paid statusdagi savdolar uchun)
    /// </summary>
    [HttpDelete("{saleId}")]
    [RequirePermission(PermissionKeys.SalesDelete)]
    public async Task<ActionResult<SaleDto>> DeleteSale(Guid saleId, CancellationToken ct = default)
    {
        // Audit actor = authenticated caller (JWT). A deleted sale reverses
        // stock + cash, so the actor MUST be recorded, never trusted from body.
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Unauthorized();

        _logger.LogInformation("DeleteSale called - Sale ID: {SaleId} by {UserId}", saleId, userId);

        try
        {
            var result = await _saleReversalService.DeleteSaleAsync(saleId, userId, ct);
            if (result.IsSuccess)
                return Ok(result.Value);
            if (result.Code == NotFoundCode)
                return NotFound();
            _logger.LogWarning("Failed to delete sale {SaleId}: {Message}", saleId, result.Error);
            return BadRequest(new { message = result.Error });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting sale {SaleId}", saleId);
            return StatusCode(500, "Savdoni o'chirishda xatolik yuz berdi");
        }
    }

    [HttpPost("{saleId}/cancel")]
    [RequirePermission(PermissionKeys.SalesDelete)]
    public async Task<ActionResult<SaleDto>> CancelSale(Guid saleId, CancellationToken ct = default)
    {
        // CRITICAL: the actor on the audit row MUST be the authenticated
        // caller — taken from the JWT, never from a client-supplied body.
        // The previous version accepted `{ adminId }` and trusted it
        // verbatim, so anyone with sales.delete could forge another admin's
        // id into the audit chain. We read the claim and drop the DTO.
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var adminId))
            return Unauthorized();

        _logger.LogInformation("CancelSale called - Sale ID: {SaleId}, Admin ID: {AdminId}", saleId, adminId);

        var result = await _saleReversalService.CancelSaleAsync(saleId, adminId, ct);
        return ToActionResult(result);
    }

    [HttpPost("{saleId}/mark-debt")]
    [RequirePermission(PermissionKeys.SalesEdit)]
    public async Task<ActionResult<SaleDto>> MarkSaleAsDebt(Guid saleId, [FromBody] MarkSaleAsDebtDto? request = null, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Unauthorized();

        _logger.LogInformation("MarkSaleAsDebt called - Sale ID: {SaleId} by {UserId}", saleId, userId);

        var result = await _saleService.MarkSaleAsDebtAsync(saleId, userId, request?.DueDate, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Update sale item price - requires role-based permissions
    /// - Open debts: All roles can edit
    /// - Closed debts: Only Owner and Admin can edit
    /// </summary>
    [HttpPatch("items/price")]
    [RequirePermission(PermissionKeys.SalesEdit)]
    public async Task<ActionResult<SaleItemDto>> UpdateSaleItemPrice([FromBody] UpdateSaleItemPriceDto request, CancellationToken ct = default)
    {
        try
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized();

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrEmpty(userRole))
                return Unauthorized();

            _logger.LogInformation("UpdateSaleItemPrice called by {UserId} with role {Role}", userId, userRole);

            if (!Guid.TryParse(request.SaleItemId, out var saleItemId))
                return BadRequest(new { message = "Noto'g'ri saleItemId formati." });

            var result = await _saleItemService.UpdateSaleItemPriceAsync(saleItemId, request, userId);
            if (result.IsSuccess)
                return Ok(result.Value);
            if (result.Code == NotFoundCode)
                return NotFound();
            return BadRequest(new { message = result.Error });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access to UpdateSaleItemPrice");
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateSaleItemPrice");
            return StatusCode(500, "Xatolik yuz berdi");
        }
    }

    [HttpPost("{saleId}/return-item")]
    [RequirePermission(PermissionKeys.SalesEdit)]
    public async Task<ActionResult<SaleItemDto?>> ReturnSaleItem(Guid saleId, [FromBody] ReturnSaleItemRequest? request, CancellationToken ct = default)
    {
        try
        {
            if (request == null)
            {
                _logger.LogWarning("ReturnSaleItem called with null request body for SaleId: {SaleId}", saleId);
                return BadRequest(new { message = "Request body cannot be null" });
            }

            // Audit actor = authenticated caller (JWT). A return refunds money,
            // so the actor MUST be recorded on the fraud-audit row.
            if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return Unauthorized();

            _logger.LogInformation("ReturnSaleItem called - SaleId: {SaleId}, SaleItemId: {SaleItemId}, Quantity: {Quantity}, User: {UserId}",
                saleId, request.SaleItemId, request.Quantity, userId);

            var result = await _saleReversalService.ReturnSaleItemAsync(saleId, request, userId);

            // result null bo'lishi mumkin (full return bo'lganda), lekin bu muvaffaqiyatli amal
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ReturnSaleItem");
            return StatusCode(500, "Tovarni qaytarishda xatolik");
        }
    }

    [HttpGet("debtors")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<IEnumerable<DebtorDto>>> GetDebtors(CancellationToken ct = default)
    {
        try
        {
            var debtors = await _saleQueryService.GetDebtorsAsync();
            return Ok(debtors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting debtors");
            return StatusCode(500, "Qarzdorlarni olishda xatolik");
        }
    }

    [HttpGet("export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.SalesExport)]
    public async Task<IActionResult> ExportSalesToExcel(
        [FromQuery] string lang = "uz",
        CancellationToken ct = default)
    {
        try
        {
            var result = await _salesExcelExportService.ExportSalesAsync(lang, CanViewCost(), CanViewProfit(), ct);
            return File(result.Content, XlsxContentType, result.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting sales");
            return StatusCode(500, "Sotuvlarni eksport qilishda xatolik");
        }
    }

    [HttpGet("export-pdf")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.SalesExport)]
    public async Task<IActionResult> ExportSalesToPdf([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string lang = "uz", CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("ExportSalesToPdf called - StartDate: {StartDate}, EndDate: {EndDate}",
                startDate, endDate);

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            _logger.LogInformation("Exporting PDF for user role: {UserRole}", userRole ?? "Unknown");

            var pdfBytes = await _reportPdfExportService.ExportSalesListToPdfAsync(startDate, endDate, CanViewCost(), CanViewProfit(), lang);

            _logger.LogInformation("Sales PDF generated successfully");

            return File(
                pdfBytes,
                "application/pdf",
                $"Sotuvlar_{_clock.NowLocal:yyyyMMdd_HHmmss}.pdf"
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation during PDF export");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting sales to PDF");
            return StatusCode(500, "Sotuvlarni PDF formatda eksport qilishda xatolik");
        }
    }

    [HttpPost("{saleId}/apply-credit")]
    [RequirePermission(PermissionKeys.SalesCreate)]
    public async Task<ActionResult<SaleDto>> ApplyCustomerCredit(Guid saleId, CancellationToken ct = default)
    {
        _logger.LogInformation("=== CONTROLLER: ApplyCustomerCredit called ===");
        _logger.LogInformation("Sale ID: {SaleId}", saleId);

        var result = await _saleService.ApplyCustomerCreditAsync(saleId);
        if (result.IsSuccess)
        {
            _logger.LogInformation("=== CONTROLLER: ApplyCustomerCredit SUCCESS ===");
            return Ok(result.Value);
        }
        if (result.Code == NotFoundCode)
            return NotFound();
        _logger.LogError("Error applying customer credit: {Message}", result.Error);
        return BadRequest(new { message = result.Error });
    }

    /// <summary>
    /// Generate and download PDF invoice for a sale
    /// </summary>
    [HttpGet("{id}/invoice")]
    [RequirePermission(PermissionKeys.SalesInvoice)]
    public async Task<IActionResult> GetInvoice(Guid id, [FromQuery] string lang = "uz", [FromQuery] bool compact = false, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("GetInvoice called - Sale ID: {SaleId}", id);

            var pdfBytes = await _reportPdfExportService.GenerateInvoicePdfAsync(id, lang, compact);

            var sale = await _saleQueryService.GetSaleByIdAsync(id);
            var fileName = $"Faktura_{id}_{_clock.NowLocal:yyyyMMdd_HHmmss}.pdf";

            _logger.LogInformation("Invoice generated successfully for sale {SaleId}", id);

            return File(
                pdfBytes,
                "application/pdf",
                fileName
            );
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Sale not found: {SaleId}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating invoice for sale {SaleId}", id);
            return StatusCode(500, "Faktura yaratishda xatolik yuz berdi");
        }
    }
}