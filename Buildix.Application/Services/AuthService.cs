using Buildix.Application.DTOs;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Security;
using Buildix.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Buildix.Domain.Interfaces;
using Buildix.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Buildix.Application.Services;

/// <summary>
/// AuthService is a <c>partial</c> class split by concern across files — the
/// type and every call-site stay unchanged (partials merge at compile time):
///   • <c>AuthService.cs</c> (this file) — constructor, fields and the shared
///     token-issuance helpers (GenerateAuthResponseAsync, GenerateRefreshToken).
///   • <c>AuthService.Login.cs</c> — password login + brute-force lockout.
///   • <c>AuthService.Refresh.cs</c> — refresh-token rotation, reuse detection
///     (IsBenignReplay / BurnFamilyAsync) and logout.
/// </summary>
public partial class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthService> _logger;
    private readonly IAppDbContext _context;
    private readonly JwtSetting _jwtSetting;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IRevokedTokenStore _revokedTokens;
    private readonly IAuditLogService _auditLogService;
    private readonly ILoginAttemptTracker _loginAttempts;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContext;

    // S6 — a real BCrypt hash we verify against when the username is unknown
    // so the response time is indistinguishable from a real-user-wrong-password
    // path (BCrypt.Verify is the dominant cost — ~100 ms at cost factor 11).
    // Computed once at type init so the value's well-formed; we never compare
    // any real input against it and the original plaintext is discarded.
    private static readonly string DummyBcryptHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public AuthService(
        IUnitOfWork unitOfWork,
        IJwtService jwtService,
        ILogger<AuthService> logger,
        IAppDbContext context,
        IConfiguration configuration,
        ICurrentMarketService currentMarketService,
        IRevokedTokenStore revokedTokens,
        IAuditLogService auditLogService,
        ILoginAttemptTracker loginAttempts,
        Microsoft.AspNetCore.Http.IHttpContextAccessor httpContext)
    {
        _unitOfWork = unitOfWork;
        _jwtService = jwtService;
        _logger = logger;
        _context = context;
        _jwtSetting = configuration.GetSection("Jwt").Get<JwtSetting>()!;
        _currentMarketService = currentMarketService;
        _revokedTokens = revokedTokens;
        _auditLogService = auditLogService;
        _loginAttempts = loginAttempts;
        _httpContext = httpContext;
    }

    /// <summary>Simplify a User-Agent into "OS · Browser" for the sessions list.</summary>
    private static string? DescribeDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        var ua = userAgent;
        string os =
            ua.Contains("Windows") ? "Windows" :
            ua.Contains("iPhone") ? "iPhone" :
            ua.Contains("iPad") ? "iPad" :
            ua.Contains("Android") ? "Android" :
            ua.Contains("Mac OS") || ua.Contains("Macintosh") ? "macOS" :
            ua.Contains("Linux") ? "Linux" : "—";
        string browser =
            ua.Contains("Edg") ? "Edge" :
            ua.Contains("OPR") || ua.Contains("Opera") ? "Opera" :
            ua.Contains("Chrome") ? "Chrome" :
            ua.Contains("Firefox") ? "Firefox" :
            ua.Contains("Safari") ? "Safari" : "—";
        return $"{os} · {browser}";
    }


    /// <param name="sessionStartedAt">
    /// Rotatsiyada ota-tokendan MEROS olingan sessiya boshlanish vaqti. null bo'lsa
    /// (login/register) — yangi sessiya boshlanadi. Rotatsiyada hech qachon
    /// yangilanmasligi shart, aks holda mutlaq sessiya limiti (MaxSessionDays)
    /// har refresh'da qaytadan boshlanib, cheksiz sessiyaga aylanadi.
    /// </param>
    private async Task<AuthResponse> GenerateAuthResponseAsync(
        User user,
        CancellationToken cancellationToken,
        DateTime? sessionStartedAt = null)
    {
        try
        {
            _logger.LogInformation("Generating tokens for user: {UserId}, {Username}", user.Id, user.Username);

            var accessToken = _jwtService.GenerateToken(user, true);
            var refreshToken = GenerateRefreshToken();

            // K1 — store only the SHA-256 hash of the refresh token. The
            // plaintext is returned to the client (line below) and lives
            // exclusively in its secure storage from this point. A DB leak
            // cannot revive any existing session.
            var http = _httpContext.HttpContext;
            var refreshTokenEntity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = RefreshTokenHasher.Hash(refreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSetting.RefreshTokenExpireDays),
                SessionStartedAt = sessionStartedAt ?? DateTime.UtcNow,
                IsUsed = false,
                IsRevoked = false,
                DeviceInfo = DescribeDevice(http?.Request.Headers.UserAgent.ToString()),
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                LastUsedAt = DateTime.UtcNow,
            };

            await _unitOfWork.RefreshTokens.AddAsync(refreshTokenEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Tokens generated successfully for user: {UserId}", user.Id);

            string languageCode = user.Language.ToCode();

            // Surface the tenant's slug so the client can match a stored session
            // to the buildix.uz/{subdomain}/ path and auto-enter without a fresh
            // login. Cheap single-column lookup; SuperAdmin (MarketId null) skips it.
            string? subdomain = null;
            if (user.MarketId.HasValue)
            {
                subdomain = await _context.Markets
                    .AsNoTracking()
                    .Where(m => m.Id == user.MarketId.Value)
                    .Select(m => m.Subdomain)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return new AuthResponse(
                user.Id,
                user.Username,
                user.FullName,
                user.Role.ToString(),
                languageCode,
                accessToken.AccessToken,
                refreshToken,
                DateTime.UtcNow.AddMinutes(_jwtSetting.AccessTokenExpireMinutes),
                user.GetEffectivePermissions(),
                user.MarketId,
                subdomain
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating tokens for user: {UserId}", user.Id);
            throw;
        }
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public async Task<IReadOnlyList<DTOs.SessionDto>> GetSessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var active = await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && !rt.IsUsed && rt.ExpiresAt > now)
            .OrderByDescending(rt => rt.LastUsedAt ?? rt.CreatedAt)
            .ToListAsync(cancellationToken);
        if (active.Count == 0) return Array.Empty<DTOs.SessionDto>();

        var currentHash = string.IsNullOrEmpty(currentRefreshToken)
            ? null
            : Buildix.Domain.Security.RefreshTokenHasher.Hash(currentRefreshToken);

        // isCurrent: exact token match, else the most-recently-used session.
        Guid currentId = currentHash is not null
            ? active.FirstOrDefault(x => x.Token == currentHash)?.Id ?? active[0].Id
            : active[0].Id;

        return active.Select(rt => new DTOs.SessionDto(
            rt.Id, rt.DeviceInfo, rt.IpAddress, rt.LastUsedAt, rt.CreatedAt, rt.Id == currentId)).ToList();
    }

    public async Task<IReadOnlyList<DTOs.LoginHistoryDto>> GetLoginHistoryAsync(Guid userId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var rows = await _context.LoginHistories.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);
        return rows.Select(h => new DTOs.LoginHistoryDto(h.Id, h.DeviceInfo, h.IpAddress, h.CreatedAt, h.Success)).ToList();
    }

    public async Task<int> RevokeOtherSessionsAsync(Guid userId, string currentRefreshToken, CancellationToken cancellationToken = default)
    {
        var currentHash = Buildix.Domain.Security.RefreshTokenHasher.Hash(currentRefreshToken);
        var now = DateTime.UtcNow;

        var others = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && !rt.IsUsed && rt.ExpiresAt > now && rt.Token != currentHash)
            .ToListAsync(cancellationToken);

        foreach (var rt in others)
        {
            rt.IsRevoked = true;
            rt.RevokedAt = now;
        }
        if (others.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
        return others.Count;
    }

    public async Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Faqat egasining aktiv sessiyasi — boshqa foydalanuvchi tokenini bekor qilib bo'lmaydi.
        var session = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Id == sessionId && rt.UserId == userId
                && !rt.IsRevoked && !rt.IsUsed && rt.ExpiresAt > now, cancellationToken);
        if (session is null) return false;

        session.IsRevoked = true;
        session.RevokedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
