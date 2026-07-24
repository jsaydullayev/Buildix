using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.Domain.Constants;
using System.Security.Claims;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class ZakupsController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IZakupService _zakupService;
    private readonly IZakupsExcelExportService _zakupsExcelExportService;
    private readonly IZakupRoleShaper _zakupShaper;
    private readonly IReorderService _reorderService;

    public ZakupsController(
        IZakupService zakupService,
        IZakupsExcelExportService zakupsExcelExportService,
        IZakupRoleShaper zakupShaper,
        IReorderService reorderService)
    {
        _zakupService = zakupService;
        _zakupsExcelExportService = zakupsExcelExportService;
        _zakupShaper = zakupShaper;
        _reorderService = reorderService;
    }

    /// <summary>«Рекомендуем заказать» — sotuv tezligiga asoslangan qayta-buyurtma tavsiyalari.</summary>
    [HttpGet("~/api/Zakups/reorder-suggestions")]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<ActionResult<IReadOnlyList<ReorderSuggestionDto>>> GetReorderSuggestions(
        [FromQuery] int limit = 20, CancellationToken ct = default)
        => Ok(await _reorderService.GetSuggestionsAsync(limit, ct));

    // Cost visibility is gated by data.costPrice (not a hardcoded role): callers
    // without it get the cost-free "seller" zakup/receipt DTOs.
    private bool CanViewCost() => HasPermission(PermissionKeys.DataCostPrice);

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetZakup(Guid id)
    {
        var zakup = await _zakupService.GetZakupByIdAsync(id);
        if (zakup is null)
            return NotFound();
        return Ok(_zakupShaper.ShapeZakup(zakup, CanViewCost()));
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetAllZakups()
    {
        var zakups = await _zakupService.GetAllZakupsAsync();
        return Ok(_zakupShaper.ShapeZakups(zakups, CanViewCost()));
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetZakupsPaged(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50)
    {
        var result = await _zakupService.GetAllZakupsPagedAsync(page, size);
        return Ok(_zakupShaper.ShapeZakupsPaged(result, CanViewCost()));
    }

    [HttpGet("by-date")]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetZakupsByDateRange([FromQuery] DateTime start, [FromQuery] DateTime end)
    {
        var zakups = await _zakupService.GetZakupsByDateRangeAsync(start, end);
        return Ok(_zakupShaper.ShapeZakups(zakups, CanViewCost()));
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.ZakupCreate)]
    public async Task<ActionResult<ZakupDto>> CreateZakup([FromBody] CreateZakupDto request)
    {
        var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(adminIdStr) || !Guid.TryParse(adminIdStr, out var adminId))
            return Unauthorized();

        try
        {
            var zakup = await _zakupService.CreateZakupAsync(request, adminId);
            return CreatedAtAction(nameof(GetZakup), new { id = zakup.Id }, zakup);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.ZakupDelete)]
    public async Task<IActionResult> DeleteZakup(Guid id)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            var deleted = await _zakupService.DeleteZakupAsync(id, userId);
            if (!deleted)
                return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // ── Goods-receipt (priyomka) — multi-item + supplier + payment ───────────

    [HttpPost]
    [RequirePermission(PermissionKeys.ZakupCreate)]
    public async Task<IActionResult> CreateReceipt([FromBody] CreateZakupReceiptDto request)
    {
        var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(adminIdStr) || !Guid.TryParse(adminIdStr, out var adminId))
            return Unauthorized();

        try
        {
            var receipt = await _zakupService.CreateZakupReceiptAsync(request, adminId);
            return Ok(_zakupShaper.ShapeReceipt(receipt, CanViewCost()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetAllReceipts()
    {
        var receipts = await _zakupService.GetAllZakupReceiptsAsync();
        return Ok(_zakupShaper.ShapeReceipts(receipts, CanViewCost()));
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetReceiptsPaged([FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        var result = await _zakupService.GetAllZakupReceiptsPagedAsync(page, size);
        return Ok(_zakupShaper.ShapeReceiptsPaged(result, CanViewCost()));
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> GetReceipt(Guid id)
    {
        var receipt = await _zakupService.GetZakupReceiptByIdAsync(id);
        if (receipt is null)
            return NotFound();
        return Ok(_zakupShaper.ShapeReceipt(receipt, CanViewCost()));
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.ZakupDelete)]
    public async Task<IActionResult> DeleteReceipt(Guid id)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            var deleted = await _zakupService.DeleteZakupReceiptAsync(id, userId);
            if (!deleted)
                return NotFound();
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}")]
    [RequirePermission(PermissionKeys.ZakupCreate)]
    public async Task<IActionResult> RegisterSupplierPayment(Guid id, [FromBody] RegisterSupplierPaymentDto request)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            var receipt = await _zakupService.RegisterSupplierPaymentAsync(id, request.Amount, userId);
            if (receipt is null)
                return NotFound();
            return Ok(_zakupShaper.ShapeReceipt(receipt, CanViewCost()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Purchase KPI tiles for the month starting at <paramref name="from"/>
    /// (Tashkent month-start as a UTC instant), aggregated DB-side.</summary>
    [HttpGet]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<ActionResult<PurchaseSummaryDto>> GetReceiptsSummary([FromQuery] DateTime from, CancellationToken ct = default)
        => Ok(await _zakupService.GetReceiptsSummaryAsync(from, ct));

    [HttpGet("export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ZakupAccess)]
    public async Task<IActionResult> ExportZakupsToExcel()
    {
        var result = await _zakupsExcelExportService.ExportZakupsAsync(CanViewCost());
        return File(result.Content, XlsxContentType, result.FileName);
    }
}
