using System.Security.Claims;
using Buildix.API.Filters;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Buildix.API.Controllers;

/// <summary>
/// SuperAdmin-only console. The URL is gated by an opaque segment that the
/// operator configures via <c>SuperAdmin:ConsoleSegment</c> — see
/// <see cref="Middleware.SuperAdminPathGateMiddleware"/>. Requests to
/// <c>/api/_sa/...</c> with the wrong segment 404 BEFORE authentication runs,
/// so an unauthenticated scanner can't even tell the console exists.
///
/// The hidden URL is defence in depth — the primary access control is the
/// JWT <c>SuperAdmin</c> role check (<see cref="AuthorizeAttribute"/>).
/// </summary>
[ApiController]
[Route("api/_sa/{consoleSegment}")]
[Authorize(Roles = "SuperAdmin")]
[EnableRateLimiting("super-admin")]
public class SuperAdminController : ControllerBase
{
    private readonly IRegistrationRequestService _service;
    private readonly ISuperAdminDashboardService _dashboard;
    private readonly ISuperAdminStoreService _stores;
    private readonly ISuperAdminBillingService _billing;
    private readonly ISuperAdminUserService _users;
    private readonly ISuperAdminSettingsService _settings;
    private readonly ISubscriptionNotifier _notifier;

    public SuperAdminController(
        IRegistrationRequestService service,
        ISuperAdminDashboardService dashboard,
        ISuperAdminStoreService stores,
        ISuperAdminBillingService billing,
        ISuperAdminUserService users,
        ISuperAdminSettingsService settings,
        ISubscriptionNotifier notifier)
    {
        _service = service;
        _dashboard = dashboard;
        _stores = stores;
        _billing = billing;
        _users = users;
        _settings = settings;
        _notifier = notifier;
    }

    /// <summary>«Панель Buildix» — 4 KPI va 3 ro'yxat bitta snapshotda.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
        => Ok(await _dashboard.GetAsync(ct));

    /// <summary>«Магазины» — do'kon markazidagi ro'yxat (tarif, muddat, holat).</summary>
    [HttpGet("stores")]
    public async Task<IActionResult> Stores(CancellationToken ct)
        => Ok(await _stores.ListAsync(ct));

    // ─── Platforma sozlamalari ──────────────────────────────────────────────

    /// <summary>Tariflar, blok qoidalari, SMS va qo'llab-quvvatlash kontaktlari.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct)
        => Ok(await _settings.GetAsync(ct));

    /// <summary>
    /// Sozlamalarni saqlaydi. Blok qoidalari SHU ZAHOTI kuchga kiradi —
    /// servis keshni yangilaydi, middleware esa har so'rovda shundan o'qiydi.
    /// </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SaUpdateSettingsDto body, CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try { return Ok(await _settings.UpdateAsync(body, superAdminId, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ─── Platforma foydalanuvchilari ────────────────────────────────────────

    /// <summary>Barcha do'konlar xodimlari — rol/do'kon/qidiruv bo'yicha filtr bilan.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users(
        [FromQuery] string? role,
        [FromQuery] int? marketId,
        [FromQuery] string? search,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct)
        => Ok(await _users.ListAsync(role, marketId, search, page <= 0 ? 1 : page, size <= 0 ? 20 : size, ct));

    /// <summary>
    /// «Сменить пароль» — SuperAdmin yangi parolni o'zi qo'yadi va shaxsan
    /// beradi (SMS orqali yuborilmaydi). Foydalanuvchining barcha sessiyalari
    /// shu zahoti uziladi.
    /// </summary>
    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id, [FromBody] SaResetPasswordDto body, CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var ok = await _users.ResetPasswordAsync(id, body.NewPassword, superAdminId, ct);
            return ok
                ? Ok(new { message = "Parol o'zgartirildi." })
                : NotFound(new { message = "Foydalanuvchi topilmadi." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("users/{id:guid}/block")]
    public Task<IActionResult> BlockUser(Guid id, CancellationToken ct)
        => SetUserActive(id, active: false, "Hisob bloklandi.", ct);

    [HttpPost("users/{id:guid}/unblock")]
    public Task<IActionResult> UnblockUser(Guid id, CancellationToken ct)
        => SetUserActive(id, active: true, "Hisob yoqildi.", ct);

    private async Task<IActionResult> SetUserActive(Guid id, bool active, string okMessage, CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var ok = await _users.SetActiveAsync(id, active, superAdminId, ct);
            return ok ? Ok(new { message = okMessage }) : NotFound(new { message = "Foydalanuvchi topilmadi." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ─── Обuna va to'lovlar ─────────────────────────────────────────────────

    /// <summary>Uch tarif kartochkasi (narx, limit, nechta do'kon).</summary>
    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct)
        => Ok(await _billing.PlansAsync(ct));

    /// <summary>«Подписки и оплаты» jadvali.</summary>
    [HttpGet("billing")]
    public async Task<IActionResult> Billing(CancellationToken ct)
        => Ok(await _billing.ListAsync(ct));

    /// <summary>
    /// «Напомнить всем должникам» — muddati o'tgan do'kon egalariga Telegram
    /// eslatmasi. Javobda nechtasiga yetib borgani va nechtasi Telegramni
    /// bog'lamagani qaytadi: bog'lanmaganlarni operator qo'ng'iroq bilan
    /// xabardor qiladi, xabar jimgina yo'qolmaydi.
    /// </summary>
    [HttpPost("reminders/overdue")]
    public async Task<IActionResult> RemindOverdue(CancellationToken ct)
        => Ok(await _notifier.RemindOverdueAsync(ct));

    /// <summary>Oxirgi to'lovlar («Последние платежи»).</summary>
    [HttpGet("payments")]
    public async Task<IActionResult> Payments([FromQuery] int take, CancellationToken ct)
        => Ok(await _billing.RecentAsync(take <= 0 ? 10 : Math.Min(take, 50), ct));

    /// <summary>
    /// To'lov natijasining oldindan ko'rinishi — modal shu sanani ko'rsatadi,
    /// keyin AYNAN shu hisob yoziladi.
    /// </summary>
    [HttpGet("markets/{marketId:int}/payment-preview")]
    public async Task<IActionResult> PaymentPreview(
        int marketId,
        [FromQuery] int months,
        [FromQuery] DateTime? expiresAt,
        CancellationToken ct)
    {
        try
        {
            var preview = await _billing.PreviewAsync(marketId, months <= 0 ? 1 : months, expiresAt, ct);
            return preview is null ? NotFound(new { message = "Do'kon topilmadi." }) : Ok(preview);
        }
        catch (InvalidOperationException ex)
        {
            // Qo'lda kiritilgan sana yaroqsiz — operator buni tugmani
            // bosishdan OLDIN ko'rishi kerak.
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// «Оплата получена» — to'lovni yozadi va obunani uzaytiradi.
    /// <c>Idempotency-Key</c> sarlavhasi bilan takroriy bosish ikki oy bermaydi.
    /// </summary>
    [HttpPost("markets/{marketId:int}/payments")]
    [Idempotent("subscription-payment", "marketId")]
    public async Task<IActionResult> RecordPayment(
        int marketId,
        [FromBody] SaRecordPaymentDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var result = await _billing.RecordAsync(marketId, body, superAdminId, ct);
            return result is null ? NotFound(new { message = "Do'kon topilmadi." }) : Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Bitta do'konning kartochkasi — detal drawer uchun.</summary>
    [HttpGet("stores/{marketId:int}")]
    public async Task<IActionResult> Store(int marketId, CancellationToken ct)
    {
        var detail = await _stores.GetAsync(marketId, ct);
        return detail is null ? NotFound(new { message = "Do'kon topilmadi." }) : Ok(detail);
    }

    [HttpGet("requests")]
    public async Task<IActionResult> ListRequests(
        [FromQuery] RegistrationRequestStatus? status,
        CancellationToken ct)
        => Ok(await _service.ListAsync(status, ct));

    [HttpGet("owners")]
    public async Task<IActionResult> ListOwners(CancellationToken ct)
        => Ok(await _service.ListOwnersAsync(ct));

    /// <summary>
    /// Real-time uniqueness check for the approve form. Any of the three fields
    /// may be omitted; each comes back as true (free), false (taken), or null
    /// (not asked). When the caller supplies a username but no subdomain, the
    /// response includes a generated <c>suggestedSubdomain</c> for preview.
    /// </summary>
    [HttpGet("check-availability")]
    public async Task<IActionResult> CheckAvailability(
        [FromQuery] string? username,
        [FromQuery] string? marketName,
        [FromQuery] string? subdomain,
        CancellationToken ct)
        => Ok(await _service.CheckAvailabilityAsync(username, marketName, subdomain, ct));

    [HttpPost("requests/{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApproveRegistrationRequestDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            return Ok(await _service.ApproveAsync(id, body, superAdminId, ct));
        }
        catch (KeyNotFoundException) { return NotFound(new { message = "So'rov topilmadi." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("requests/{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectRegistrationRequestDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var ok = await _service.RejectAsync(id, body.Reason, superAdminId, ct);
            return ok ? Ok(new { message = "Rad etildi." }) : NotFound(new { message = "So'rov topilmadi." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// «Принять» — operator arizachiga qo'ng'iroq qildi. Do'kon YARATILMAYDI:
    /// ariza «yangi»lar ro'yxatidan chiqadi, lekin hali ulanmagan. Do'kon
    /// yaratish — alohida qadam (<c>approve</c>).
    /// </summary>
    [HttpPost("requests/{id:guid}/accept")]
    public Task<IActionResult> Accept(Guid id, CancellationToken ct)
        => SetStatus(id, RegistrationRequestStatus.Accepted, "Qabul qilindi.", ct);

    /// <summary>«Вернуть» — qabul qilish yoki rad etishni bekor qiladi.</summary>
    [HttpPost("requests/{id:guid}/reopen")]
    public Task<IActionResult> Reopen(Guid id, CancellationToken ct)
        => SetStatus(id, RegistrationRequestStatus.Pending, "Qaytarildi.", ct);

    private async Task<IActionResult> SetStatus(
        Guid id, RegistrationRequestStatus target, string okMessage, CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var ok = await _service.SetStatusAsync(id, target, superAdminId, ct);
            return ok ? Ok(new { message = okMessage }) : NotFound(new { message = "So'rov topilmadi." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ─── Owner CRUD ─────────────────────────────────────────────────────────

    /// <summary>Full owner profile (Owner + Market + live stats).</summary>
    [HttpGet("owners/{id:guid}")]
    public async Task<IActionResult> GetOwner(Guid id, CancellationToken ct)
    {
        var detail = await _service.GetOwnerDetailAsync(id, ct);
        return detail is null
            ? NotFound(new { message = "Owner topilmadi." })
            : Ok(detail);
    }

    /// <summary>Manual create — same shape as Approve, no backing request.</summary>
    [HttpPost("owners")]
    public async Task<IActionResult> CreateOwner(
        [FromBody] CreateOwnerDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            return Ok(await _service.CreateOwnerAsync(body, superAdminId, ct));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Update Owner+Market mutable fields. Username/password are not editable here.</summary>
    [HttpPut("owners/{id:guid}")]
    public async Task<IActionResult> UpdateOwner(
        Guid id,
        [FromBody] UpdateOwnerDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            return Ok(await _service.UpdateOwnerAsync(id, body, superAdminId, ct));
        }
        catch (KeyNotFoundException) { return NotFound(new { message = "Owner topilmadi." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Soft-delete: Owner → IsDeleted, Market → IsActive=false. Historical data preserved.</summary>
    [HttpDelete("owners/{id:guid}")]
    public async Task<IActionResult> DeleteOwner(
        Guid id,
        [FromBody] DeleteOwnerDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var ok = await _service.DeleteOwnerAsync(id, body, superAdminId, ct);
            return ok ? Ok(new { message = "Owner o'chirildi." }) : NotFound(new { message = "Owner topilmadi." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ─── Market block / unblock ─────────────────────────────────────────────

    /// <summary>
    /// Block a market — all login/tenant-resolution attempts return 423 until
    /// unblocked. Primary use: subscription non-payment. Reversible.
    /// </summary>
    [HttpPost("markets/{marketId:int}/block")]
    public async Task<IActionResult> BlockMarket(
        int marketId,
        [FromBody] BlockMarketDto body,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            var result = await _service.BlockMarketAsync(marketId, body, superAdminId, ct);
            // Ega bloklanganini BILISHI kerak — aks holda panelga kirolmay,
            // sababini ham bilmay qoladi. Best-effort: xabar yetib bormasa
            // ham blok kuchda qoladi.
            await _notifier.NotifyBlockedAsync(marketId, body.Reason, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException) { return NotFound(new { message = "Do'kon topilmadi." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("markets/{marketId:int}/unblock")]
    public async Task<IActionResult> UnblockMarket(
        int marketId,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();
        try
        {
            return Ok(await _service.UnblockMarketAsync(marketId, superAdminId, ct));
        }
        catch (KeyNotFoundException) { return NotFound(new { message = "Do'kon topilmadi." }); }
    }

    /// <summary>
    /// Do'kon kompyuterini bog'lash uchun bir martalik kod beradi.
    ///
    /// <para>Texnik do'konga borganda uni ilovaga kiritadi va kompyuter
    /// bulutdan o'z kalitini oladi. Kod bir sutka yashaydi va bir marta
    /// ishlaydi; yangisi olinsa eskisi darhol o'ladi.</para>
    /// </summary>
    [HttpPost("markets/{marketId:int}/pairing-code")]
    public async Task<IActionResult> IssuePairingCode(
        int marketId,
        [FromServices] Buildix.Application.Interfaces.ITerminalPairingService pairing,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();

        var result = await pairing.IssueCodeAsync(marketId, superAdminId, ct);
        if (result.IsFailure)
        {
            return result.Code == "NOT_FOUND"
                ? NotFound(new { message = result.Error })
                : BadRequest(new { message = result.Error });
        }
        return Ok(result.Value);
    }

    /// <summary>Do'konga bog'langan kompyuterlar.</summary>
    [HttpGet("markets/{marketId:int}/terminals")]
    public async Task<IActionResult> ListTerminals(
        int marketId,
        [FromServices] Buildix.Application.Interfaces.ITerminalPairingService pairing,
        CancellationToken ct)
        => Ok(await pairing.ListAsync(marketId, ct));

    /// <summary>
    /// Kompyuterni bulutdan uzadi.
    ///
    /// <para>Kompyuter almashtirilganda yoki yo'qolganda ishlatiladi. Bu
    /// yangi kompyuterni bog'lashning YAGONA yo'li ham: bitta do'konga bir
    /// vaqtda faqat bitta kompyuter bog'lanadi.</para>
    /// </summary>
    [HttpPost("terminals/{terminalId:guid}/revoke")]
    public async Task<IActionResult> RevokeTerminal(
        Guid terminalId,
        [FromServices] Buildix.Application.Interfaces.ITerminalPairingService pairing,
        CancellationToken ct)
    {
        if (!TryGetCallerId(out var superAdminId)) return Unauthorized();

        var result = await pairing.RevokeAsync(terminalId, superAdminId, ct);
        if (result.IsFailure) return NotFound(new { message = result.Error });
        return Ok(new { revoked = true });
    }

    private bool TryGetCallerId(out Guid id)
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out id);
    }
}
