using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.Domain.Constants;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class CustomersController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ICustomerService _customerService;
    private readonly ICustomersExcelExportService _customersExcelExportService;

    public CustomersController(ICustomerService customerService, ICustomersExcelExportService customersExcelExportService)
    {
        _customerService = customerService;
        _customersExcelExportService = customersExcelExportService;
    }

    [HttpGet("{id}")]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<CustomerDto>> GetCustomer(Guid id, CancellationToken ct)
    {
        var customer = await _customerService.GetCustomerByIdAsync(id);
        if (customer is null)
            return NotFound();

        return Ok(customer);
    }

    [HttpGet("phone/{phone}")]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<CustomerDto>> GetCustomerByPhone(string phone, CancellationToken ct)
    {
        var customer = await _customerService.GetCustomerByPhoneAsync(phone);
        if (customer is null)
            return NotFound();

        return Ok(customer);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<IEnumerable<CustomerDto>>> GetAllCustomers(CancellationToken ct)
    {
        var customers = await _customerService.GetAllCustomersAsync(ct);
        return Ok(customers);
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<PagedResult<CustomerDto>>> GetCustomersPaged(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50,
        [FromQuery] string? search = null,
        [FromQuery] bool? withDebt = null,
        [FromQuery] string? customerType = null,
        [FromQuery] bool? isRegular = null,
        CancellationToken ct = default)
    {
        var result = await _customerService.GetAllCustomersPagedAsync(page, size, search, withDebt, customerType, isRegular, ct);
        return Ok(result);
    }

    /// <summary>Mijozning oxirgi xaridlari — Admin Клиенты detal oynasi «Последние покупки».</summary>
    [HttpGet("~/api/Customers/{id}/purchases")]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<IReadOnlyList<CustomerPurchaseDto>>> GetCustomerPurchases(
        Guid id, [FromQuery] int limit = 10, CancellationToken ct = default)
        => Ok(await _customerService.GetCustomerPurchasesAsync(id, limit, ct));

    [HttpPost]
    [RequirePermission(PermissionKeys.CustomersManage)]
    public async Task<ActionResult<CustomerDto>> CreateCustomer([FromBody] CreateCustomerDto request, CancellationToken ct)
    {
        var result = await _customerService.CreateCustomerAsync(request);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(GetCustomerByPhone), new { phone = result.Value.Phone }, result.Value);
    }

    [HttpPut]
    [RequirePermission(PermissionKeys.CustomersManage)]
    public async Task<ActionResult<CustomerDto>> UpdateCustomer([FromBody] UpdateCustomerDto request, CancellationToken ct)
    {
        var result = await _customerService.UpdateCustomerAsync(request);
        if (result.IsFailure)
            return result.Code == NotFoundCode ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    [RequirePermission(PermissionKeys.CustomersDelete)]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken ct)
    {
        var result = await _customerService.DeleteCustomerAsync(id);
        if (!result)
            return NotFound();

        return NoContent();
    }

    [HttpGet("{id}/delete-info")]
    [RequirePermission(PermissionKeys.CustomersAccess)]
    public async Task<ActionResult<CustomerDeleteInfoDto>> GetCustomerDeleteInfo(Guid id, CancellationToken ct)
    {
        var deleteInfo = await _customerService.GetCustomerDeleteInfoAsync(id);
        return Ok(deleteInfo);
    }

    [HttpPost("{id}/soft-delete")]
    [RequirePermission(PermissionKeys.CustomersDelete)]
    public async Task<IActionResult> SoftDeleteCustomer(Guid id)
    {
        var result = await _customerService.SoftDeleteCustomerAsync(id);
        if (!result)
            return NotFound();

        return Ok(new { message = "Customer soft deleted" });
    }

    /// <summary>
    /// Export all customers as an Excel spreadsheet. Mirrors the
    /// /api/Products/.../export pattern so the same DownloadService
    /// helper handles both files on the Flutter side.
    /// </summary>
    [HttpGet("export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.CustomersExport)]
    public async Task<IActionResult> ExportCustomersToExcel(
        [FromQuery] string lang = "uz",
        CancellationToken ct = default)
    {
        var result = await _customersExcelExportService.ExportCustomersAsync(lang, ct);
        return File(result.Content, XlsxContentType, result.FileName);
    }
}
