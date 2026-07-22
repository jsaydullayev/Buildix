using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);

            if (result is null)
            {
                _logger.LogWarning("Login FAILED for user: {Username}", request.Username);
                return Unauthorized("Invalid credentials");
            }

            _logger.LogInformation("Login SUCCESS for user: {Username}", result.Username);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Shift-inactive (and similar) rejections carry a user-facing
            // message; surface it as 400 so the login screen can show it.
            _logger.LogWarning("Login rejected for {Username}: {Reason}", request.Username, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    // NOTE: public self-registration (POST /api/Auth/Register) was intentionally
    // removed. Onboarding is SuperAdmin-gated only: a visitor submits a contact
    // request (POST /api/RegistrationRequests) and the SuperAdmin approves it
    // (creating the Owner + Market + subdomain) or creates the owner manually.
    // See RegistrationRequestsController / SuperAdminController.

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request);

        // 401 — bitta umumiy javob: qaysi sabab (noma'lum/muddati o'tgan/begona/
        // o'g'irlangan) ekanini oshkor qilmaymiz (user enumeration'ni oldini olish).
        //
        // Eslatma: rotatsiya poygasi (ikki tab) va "javob yo'lda yo'qoldi" holatlari
        // bu yergacha YETIB KELMAYDI — AuthService ularni grace oynasi ichida
        // xayrixoh deb tanib, o'sha zanjirdan yangi juftlik beradi. Shuning uchun
        // bu yerda alohida 409 shoxi yo'q.
        if (result is null)
            return Unauthorized("Invalid token");

        return Ok(result);
    }

    [HttpPost]
    [Authorize]
    [EnableRateLimiting("auth-logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        // Pull jti + expiry from the current access token's claims (the JwtBearer
        // middleware already populated User.Claims). Passing them down lets
        // AuthService add this exact token to the revocation list — without it,
        // the access token would still be usable until its natural 30-min TTL.
        var jti = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        DateTime? expiresAt = null;
        var expClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp)?.Value;
        if (long.TryParse(expClaim, out var unix))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        }

        var result = await _authService.LogoutAsync(request.RefreshToken, userId, jti, expiresAt);

        if (!result)
            return BadRequest("Invalid token");

        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>The caller's active sessions ("Устройства и сессии").</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();
        return Ok(await _authService.GetSessionsAsync(userId, null, ct));
    }

    /// <summary>«Завершить все другие сессии» — revoke every session but this one.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> RevokeOtherSessions([FromBody] RevokeOtherSessionsRequest request, CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();
        var count = await _authService.RevokeOtherSessionsAsync(userId, request.RefreshToken, ct);
        return Ok(new { revoked = count });
    }

    /// <summary>Account "Последние входы" — the caller's recent sign-in attempts.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<LoginHistoryDto>>> LoginHistory([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();
        return Ok(await _authService.GetLoginHistoryAsync(userId, limit, ct));
    }
}
