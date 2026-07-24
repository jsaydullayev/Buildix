using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.Domain.Constants;
using System.Security.Claims;
using Buildix.Domain.Enums;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class SuppliersController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ISupplierService _supplierService;
    private readonly ISuppliersExcelExportService _suppliersExcelExportService;
    private readonly IZakupService _zakupService;

    public SuppliersController(ISupplierService supplierService, ISuppliersExcelExportService suppliersExcelExportService, IZakupService zakupService)
    {
        _supplierService = supplierService;
        _suppliersExcelExportService = suppliersExcelExportService;
        _zakupService = zakupService;
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.SuppliersAccess)]
    public async Task<ActionResult<SupplierDto>> GetSupplier(Guid id, CancellationToken ct)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var supplier = await _supplierService.GetSupplierByIdAsync(id, userRole, ct);
        if (supplier is null)
            return NotFound();
        return Ok(supplier);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.SuppliersAccess)]
    public async Task<ActionResult<IEnumerable<SupplierDto>>> GetAllSuppliers(CancellationToken ct)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        return Ok(await _supplierService.GetAllSuppliersAsync(userRole, ct));
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.SuppliersAccess)]
    public async Task<ActionResult<PagedResult<SupplierDto>>> GetSuppliersPaged(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        return Ok(await _supplierService.GetAllSuppliersPagedAsync(page, size, search, userRole, ct));
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.SuppliersManage)]
    public async Task<ActionResult<SupplierDto>> CreateSupplier([FromBody] CreateSupplierDto request, CancellationToken ct)
    {
        var result = await _supplierService.CreateSupplierAsync(request, ct);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(GetSupplier), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut]
    [RequirePermission(PermissionKeys.SuppliersManage)]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier([FromBody] UpdateSupplierDto request, CancellationToken ct)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var result = await _supplierService.UpdateSupplierAsync(request, userRole, ct);
        if (result.IsFailure)
            return result.Code == NotFoundCode ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(result.Value);
    }

    /// <summary>«Погасить долг» — yetkazuvchi qarzini FIFO (eng eski chekdan) yopadi.</summary>
    [HttpPost("~/api/Suppliers/{id}/pay-debt")]
    [RequirePermission(PermissionKeys.ZakupCreate)]
    public async Task<IActionResult> PaySupplierDebt(Guid id, [FromBody] RegisterSupplierPaymentDto request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Unauthorized();

        var result = await _zakupService.PaySupplierDebtFifoAsync(id, request.Amount, userId, ct);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error });
        return Ok(new { paid = result.Value });
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.SuppliersDelete)]
    public async Task<IActionResult> DeleteSupplier(Guid id, CancellationToken ct)
    {
        var ok = await _supplierService.SoftDeleteSupplierAsync(id, ct);
        if (!ok)
            return NotFound();
        return NoContent();
    }

    [HttpGet("{id}/delete-info")]
    [RequirePermission(PermissionKeys.SuppliersAccess)]
    public async Task<ActionResult<SupplierDeleteInfoDto>> GetSupplierDeleteInfo(Guid id, CancellationToken ct)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var info = await _supplierService.GetSupplierDeleteInfoAsync(id, userRole, ct);
        return Ok(info);
    }

    [HttpGet("export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.SuppliersAccess)]
    public async Task<IActionResult> ExportSuppliersToExcel(
        [FromQuery] string lang = "uz",
        CancellationToken ct = default)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var result = await _suppliersExcelExportService.ExportSuppliersAsync(lang, userRole, ct);
        return File(result.Content, XlsxContentType, result.FileName);
    }
}
