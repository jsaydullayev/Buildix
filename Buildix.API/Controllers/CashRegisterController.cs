using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.API.Authorization;
using Buildix.API.Filters;
using Buildix.Domain.Constants;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CashRegisterController : ControllerBase
{
    private readonly ICashRegisterService _cashRegisterService;

    public CashRegisterController(ICashRegisterService cashRegisterService)
    {
        _cashRegisterService = cashRegisterService;
    }

    [HttpGet]
    [RequirePermission(PermissionKeys.CashRegisterAccess)]
    public async Task<ActionResult<CashRegisterDto>> GetCashRegister(CancellationToken cancellationToken)
    {
        var result = await _cashRegisterService.GetCashRegisterAsync(cancellationToken);
        if (result == null)
            return NotFound(new { message = "Cash register not found" });

        return Ok(result);
    }

    [HttpGet("today-sales")]
    [RequirePermission(PermissionKeys.CashRegisterAccess)]
    public async Task<ActionResult<TodaySalesSummaryDto>> GetTodaySales(CancellationToken cancellationToken)
    {
        var result = await _cashRegisterService.GetTodaySalesSummaryAsync(cancellationToken);
        if (result == null)
            return NotFound(new { message = "Today's sales summary not found" });

        return Ok(result);
    }

    /// <summary>Касса kunlik ledger'i — balans + приход/расход + tiplangan
    /// harakatlar ro'yxati. date (local kun) berilmasa — bugun.</summary>
    [HttpGet("movements")]
    [RequirePermission(PermissionKeys.CashRegisterAccess)]
    public async Task<ActionResult<CashLedgerDto>> GetCashLedger(
        [FromQuery] DateTime? date = null, CancellationToken cancellationToken = default)
        => Ok(await _cashRegisterService.GetCashLedgerAsync(date, cancellationToken));

    [HttpPost("withdraw")]
    [RequirePermission(PermissionKeys.CashRegisterManage)]
    [Idempotent("cash-withdraw")]
    public async Task<IActionResult> WithdrawCash([FromBody] WithdrawCashRequest request, CancellationToken cancellationToken)
    {
        // User ID ni JWT dan olamiz
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user" });
        }

        var result = await _cashRegisterService.WithdrawCashAsync(request, userId, cancellationToken);
        if (result.IsFailure)
            return BadRequest(new { message = result.Error, code = result.Code });

        return Ok(new { message = "Cash withdrawn successfully" });
    }

    /// <summary>Admin submits a cash-withdrawal request for owner approval.
    /// Records intent only — no balance change until approved.</summary>
    [HttpPost("withdraw-request")]
    [RequirePermission(PermissionKeys.CashRegisterManage)]
    [Idempotent("cash-withdraw-request")]
    public async Task<IActionResult> RequestWithdrawal([FromBody] WithdrawCashRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized(new { message = "Invalid user" });
        var result = await _cashRegisterService.RequestWithdrawalAsync(request, userId, cancellationToken);
        if (result.IsFailure) return BadRequest(new { message = result.Error });
        return Ok(new { message = "Запрос отправлен" });
    }

    /// <summary>Owner approves a pending withdrawal — debits the till.</summary>
    [HttpPost("withdrawals/{id:guid}/approve")]
    [Authorize(Roles = "Owner,SuperAdmin")]
    public async Task<IActionResult> ApproveWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized(new { message = "Invalid user" });
        var result = await _cashRegisterService.ApproveWithdrawalAsync(id, userId, cancellationToken);
        if (result.IsFailure) return BadRequest(new { message = result.Error, code = result.Code });
        return Ok(new { message = "Одобрено" });
    }

    /// <summary>Owner rejects a pending withdrawal.</summary>
    [HttpPost("withdrawals/{id:guid}/reject")]
    [Authorize(Roles = "Owner,SuperAdmin")]
    public async Task<IActionResult> RejectWithdrawal(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized(new { message = "Invalid user" });
        var result = await _cashRegisterService.RejectWithdrawalAsync(id, userId, cancellationToken);
        if (result.IsFailure) return BadRequest(new { message = result.Error });
        return Ok(new { message = "Отклонено" });
    }

    /// <summary>Withdrawal requests (filter by status: Pending/Approved/Rejected).</summary>
    [HttpGet("withdrawals")]
    [RequirePermission(PermissionKeys.CashRegisterAccess)]
    public async Task<ActionResult<IReadOnlyList<WithdrawalListItemDto>>> GetWithdrawals(
        [FromQuery] string? status = null, CancellationToken cancellationToken = default)
        => Ok(await _cashRegisterService.GetWithdrawalsAsync(status, cancellationToken));

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    [HttpPost("add")]
    [RequirePermission(PermissionKeys.CashRegisterManage)]
    [Idempotent("cash-add")]
    public async Task<IActionResult> AddCash([FromBody] AddCashRequest request, CancellationToken cancellationToken)
    {
        // Y3 — capture the JWT identity so the deposit audit row attributes
        // the actor, same as WithdrawCash above.
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Invalid user" });
        }

        var success = await _cashRegisterService.AddCashAsync(request.Amount, userId, cancellationToken);
        if (!success)
            return BadRequest(new { message = "Failed to add cash" });

        return Ok(new { message = "Cash added successfully" });
    }
}
