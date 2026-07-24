using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Buildix.API.Authorization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using System.Security.Claims;

namespace Buildix.API.Controllers;

/// <summary>
/// Seller work-shift sessions. Self-service — every authenticated user opens,
/// closes and views only their OWN shift (the user id comes from the JWT), so
/// these endpoints are gated by plain [Authorize], not a permission.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _shiftService;

    public ShiftsController(IShiftService shiftService) => _shiftService = shiftService;

    private Guid? CurrentUserId()
        => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>The caller's currently open shift; 204 No Content when none.</summary>
    [HttpGet("current")]
    public async Task<ActionResult<ShiftDto>> GetCurrent(CancellationToken ct = default)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        var shift = await _shiftService.GetCurrentShiftAsync(userId, ct);
        return shift is null ? NoContent() : Ok(shift);
    }

    /// <summary>Opens the caller's work shift (idempotent).</summary>
    [HttpPost("open")]
    public async Task<ActionResult<ShiftDto>> Open(CancellationToken ct = default)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        return Ok(await _shiftService.OpenShiftAsync(userId, ct));
    }

    /// <summary>Closes the caller's open work shift, reconciling the drawer
    /// against the counted cash when provided.</summary>
    [HttpPost("close")]
    public async Task<ActionResult<ShiftDto>> Close([FromBody] CloseShiftRequest? request, CancellationToken ct = default)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        try
        {
            return Ok(await _shiftService.CloseShiftAsync(userId, request?.CountedCash, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>The caller's OWN shift history + period totals — the seller
    /// Смены screen. Self-service like current/open/close: a Seller does not hold
    /// users.shift, so the market-wide endpoints below are closed to them.
    /// <paramref name="range"/>: week (default) | month | all.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<MyShiftsDto>> GetMyShifts(
        [FromQuery] string? range = null, CancellationToken ct = default)
    {
        if (CurrentUserId() is not { } userId) return Unauthorized();
        return Ok(await _shiftService.GetMyShiftsAsync(userId, range, ct));
    }

    /// <summary>Market-wide shift history (all cashiers) — Смены history table.
    /// Owner/Admin gated by users.shift; market-scoped inside the service.
    /// Optional filters: <paramref name="userId"/> (one cashier) and a UTC
    /// [from, to) range.</summary>
    [HttpGet]
    [RequirePermission(PermissionKeys.UsersShift)]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> GetMarketShifts(
        [FromQuery] int limit = 30,
        [FromQuery] Guid? userId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
        => Ok(await _shiftService.GetMarketShiftsAsync(limit, userId, from, to, ct));

    /// <summary>Force-close another cashier's open shift (Owner/Admin,
    /// users.shift). Used when a seller leaves without closing. The counted
    /// cash is entered by the Owner; the audit row records who forced it.</summary>
    [HttpPost("{shiftId:guid}/force-close")]
    [RequirePermission(PermissionKeys.UsersShift)]
    public async Task<ActionResult<ShiftDto>> ForceClose(
        Guid shiftId, [FromBody] CloseShiftRequest? request, CancellationToken ct = default)
    {
        if (CurrentUserId() is not { } adminId) return Unauthorized();
        try
        {
            return Ok(await _shiftService.ForceCloseShiftAsync(shiftId, adminId, request?.CountedCash, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>A specific user's worked-shift sessions (most recent first).
    /// Owner/Admin only — gated by users.shift — so an Owner can review how
    /// long a seller actually worked. Market-scoped inside the service.</summary>
    [HttpGet("user/{userId:guid}")]
    [RequirePermission(PermissionKeys.UsersShift)]
    public async Task<ActionResult<IReadOnlyList<ShiftDto>>> GetUserShifts(
        Guid userId, CancellationToken ct = default)
        => Ok(await _shiftService.GetUserShiftsAsync(userId, 30, ct));
}
