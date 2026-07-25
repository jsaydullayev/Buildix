using System.Security.Claims;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Enums;
using Buildix.API.Authorization;
using Buildix.API.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
[Authorize]
public class DebtsController : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IDebtService _debts;
    private readonly IDebtQueryService _debtQueries;
    private readonly IDebtsExcelExportService _excelExport;

    public DebtsController(IDebtService debts, IDebtQueryService debtQueries, IDebtsExcelExportService excelExport)
    {
        _debts = debts;
        _debtQueries = debtQueries;
        _excelExport = excelExport;
    }

    /// <summary>Долги — Excel export (debtor summary).</summary>
    [HttpGet("~/api/Debts/export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<IActionResult> ExportDebtsToExcel([FromQuery] string lang = "uz", CancellationToken ct = default)
    {
        var result = await _excelExport.ExportDebtsAsync(lang, ct);
        return File(result.Content, XlsxContentType, result.FileName);
    }

    /// <summary>Долги ekrani: mijoz bo'yicha jamlangan qarzdorlar (search + due filter).</summary>
    [HttpGet("~/api/Debts/debtors")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<IReadOnlyList<DebtorSummaryDto>>> GetDebtors(
        [FromQuery] string? search = null,
        [FromQuery] string? due = null,
        CancellationToken ct = default)
        => Ok(await _debtQueries.GetDebtorSummariesAsync(search, due, ct));

    /// <summary>Долги sarlavhasidagi stat kartalar.</summary>
    [HttpGet("~/api/Debts/summary")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<DebtSummaryStatsDto>> GetSummary(CancellationToken ct = default)
        => Ok(await _debtQueries.GetSummaryStatsAsync(ct));

    /// <summary>«Принятые сегодня» — bugungi qabul qilingan qarz-to'lovlari.</summary>
    [HttpGet("~/api/Debts/payments/today")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<IReadOnlyList<DebtPaymentTodayDto>>> GetTodayPayments(CancellationToken ct = default)
        => Ok(await _debtQueries.GetTodayPaymentsAsync(ct));

    /// <summary>Open debts for one customer.</summary>
    [HttpGet("{customerId}")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<IEnumerable<DebtDto>>> GetCustomerDebts(Guid customerId, CancellationToken ct)
        => Ok(await _debtQueries.GetByCustomerAsync(customerId, ct));

    /// <summary>Total open debt for one customer.</summary>
    [HttpGet("~/api/Debts/customer/{customerId}/total")]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<decimal>> GetCustomerTotalDebt(Guid customerId, CancellationToken ct)
        => Ok(await _debtQueries.GetCustomerTotalAsync(customerId, ct));

    /// <summary>Make a payment against an open debt.</summary>
    [HttpPost("~/api/Debts/{debtId}/pay")]
    [RequirePermission(PermissionKeys.DebtsManage)]
    [Idempotent("debt-payment")]
    public async Task<IActionResult> PayDebt(Guid debtId, [FromBody] PayDebtDto request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return Unauthorized();

        var result = await _debts.PayAsync(debtId, request, userId, ct);
        if (result.IsFailure)
            return result.Code == NotFoundCode
                ? NotFound(new { message = result.Error })
                : BadRequest(new { message = result.Error });

        var pay = result.Value;
        return Ok(new
        {
            message = "Payment successful",
            remainingDebt = pay.RemainingDebt,
            paymentAmount = pay.PaymentAmount,
            debtStatus = pay.DebtStatus
        });
    }

    /// <summary>Update a debt's payment due date. Requires debts.dueDate.</summary>
    [HttpPut("~/api/Debts/{debtId}/due-date")]
    [RequirePermission(PermissionKeys.DebtsDueDate)]
    public async Task<ActionResult<DebtDto>> UpdateDueDate(Guid debtId, [FromBody] UpdateDebtDueDateDto request, CancellationToken ct)
    {
        var result = await _debts.UpdateDueDateAsync(debtId, request.DueDate, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(new { message = result.Error });
    }

    /// <summary>All debts in this market (optionally filtered by status).</summary>
    [HttpGet]
    [RequirePermission(PermissionKeys.DebtsAccess)]
    public async Task<ActionResult<IEnumerable<DebtDto>>> GetAllDebts(
        [FromQuery] DebtStatus? status,
        CancellationToken ct)
        => Ok(await _debtQueries.ListAsync(status, ct));
}
