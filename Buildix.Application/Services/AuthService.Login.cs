using Buildix.Application.DTOs;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Buildix.Domain.Interfaces;
using Buildix.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Buildix.Application.Services;

// Part of the AuthService partial class — password login + brute-force lockout
public partial class AuthService
{
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Login attempt for username: {Username}", request.Username);

        // Audit a failed authentication attempt. The username may not map to any
        // real account, so userId is unknown — Guid.Empty is used and the username
        // travels in the payload; the client IP is captured by AuditLogService.
        // LogActionAsync swallows its own errors, so a failed audit never masks
        // the 401 nor breaks the login flow.
        Task LogLoginFailedAsync() => _auditLogService.LogActionAsync(
            AuditEntityTypes.Auth, Guid.Empty, AuditActions.LoginFailed, Guid.Empty,
            new { username = request.Username }, cancellationToken);

        // Record a failure against the brute-force tracker AND audit-log it.
        // If this failure tripped the threshold, surface a LoginLockedException
        // so the client sees "try again in N minutes" instead of yet another
        // generic 401 — and so an attacker enumerating passwords learns we
        // notice. The IP-based rate limiter still gates burst rates; this
        // catches a distributed attacker hitting one username from many IPs.
        async Task<AuthResponse?> RejectAndMaybeLockAsync()
        {
            await LogLoginFailedAsync();
            var attempt = await _loginAttempts.RecordFailureAsync(request.Username, cancellationToken);
            if (attempt.LockedUntilUtc is { } lockedUntil)
            {
                _logger.LogWarning(
                    "Account locked after {Count} failures: {Username} until {Until:O}",
                    attempt.FailureCount, request.Username, lockedUntil);
                throw new Buildix.Domain.Exceptions.LoginLockedException(
                    request.Username, lockedUntil, attempt.FailureCount);
            }
            return null;
        }

        // Cheap lock check before touching the DB — a locked username should
        // not be able to keep timing the password hash.
        var lockedUntilUtc = _loginAttempts.GetLockedUntilUtc(request.Username);
        if (lockedUntilUtc is { } existingLockUntil)
        {
            _logger.LogWarning(
                "Login refused — account already locked until {Until:O}: {Username}",
                existingLockUntil, request.Username);
            await LogLoginFailedAsync();
            throw new Buildix.Domain.Exceptions.LoginLockedException(
                request.Username, existingLockUntil, 0);
        }

        // Resolve the market from the login-URL slug when the client sends one
        // (buildix.uz/{subdomain}/login). The slug does two things BEFORE we
        // touch credentials: (1) gates on the tenant's subscription door so an
        // expired/blocked storefront shows the right screen instead of a
        // generic 401, and (2) scopes the candidate lookup to that one market —
        // which disambiguates usernames that collide across tenants.
        Market? slugMarket = null;
        var slug = request.Subdomain?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(slug))
        {
            slugMarket = await _context.Markets
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Subdomain == slug, cancellationToken);

            if (slugMarket is null || !slugMarket.IsActive)
            {
                // Unknown / deactivated storefront — a wrong slug is a wrong
                // door. Return the same generic failure a bad password gets so
                // the endpoint reveals nothing about which markets exist.
                _logger.LogWarning("Login slug not found or inactive: {Slug}", slug);
                return await RejectAndMaybeLockAsync();
            }

            // Block / expiry are tenant-level, already public via the market
            // state endpoint — safe to signal before credential verification.
            EnsureMarketOpen(slugMarket);
        }

        var candidates = (slugMarket is null
            ? await _unitOfWork.Users.FindAsync(
                u => u.Username == request.Username && u.IsActive,
                cancellationToken)
            : await _unitOfWork.Users.FindAsync(
                u => u.Username == request.Username && u.IsActive && u.MarketId == slugMarket.Id,
                cancellationToken)).ToList();

        if (candidates.Count == 0)
        {
            // S6 — timing-attack mitigation. Without this, a missing-username
            // path returns ~instantly while a real-username-wrong-password
            // path takes the BCrypt.Verify hit (~100ms). That gap lets an
            // attacker enumerate valid usernames just by measuring latency.
            // Burn one verify against a fixed dummy hash so both paths spend
            // roughly the same wall time. We discard the result.
            _ = BCrypt.Net.BCrypt.Verify(request.Password, DummyBcryptHash);
            _logger.LogWarning("User not found or inactive: {Username}", request.Username);
            return await RejectAndMaybeLockAsync();
        }

        // P7 — short-circuit on the first BCrypt match. The old code verified
        // EVERY candidate against the same password just to detect the
        // "two users share both username AND password" edge case — astronomically
        // unlikely once you account for BCrypt's per-row salt, and worth at most
        // a warning, not a 100 ms × N hit on every login. With N=5 cross-tenant
        // username collisions that was a half-second wasted on every successful
        // login.
        if (candidates.Count > 1)
        {
            _logger.LogWarning(
                "Login {Username} resolved to {Count} candidate users — username collides across tenants",
                request.Username, candidates.Count);
        }
        User? matched = null;
        foreach (var candidate in candidates)
        {
            if (BCrypt.Net.BCrypt.Verify(request.Password, candidate.PasswordHash))
            {
                matched = candidate;
                break;
            }
        }

        if (matched is null)
        {
            _logger.LogWarning("Invalid password for user: {Username}", request.Username);
            // Wrong password for a KNOWN account — record it in the user's sign-in
            // history ("Неверный пароль"). Attribute to the primary candidate.
            await RecordLoginHistoryAsync(candidates[0].Id, false, cancellationToken);
            return await RejectAndMaybeLockAsync();
        }

        // Market door check for the no-slug path (SuperAdmin / market-agnostic
        // login). The slug path already gated the exact same market up front,
        // so we skip the re-fetch there. SuperAdmin (MarketId=null) bypasses
        // this and can always reach the console even if every tenant is shut.
        // The 402/423 bodies (built by the global exception handler) carry the
        // expiry/reason so the client can render the right screen.
        if (slugMarket is null && matched.MarketId.HasValue)
        {
            var market = await _context.Markets
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matched.MarketId.Value, cancellationToken);
            if (market is not null)
                EnsureMarketOpen(market);
        }

        // Shift gate — a Seller whose shift is Blocked, or outside its
        // scheduled window, may not log in. Owner/Admin/SuperAdmin have no
        // shift so they are never gated here.
        if (matched.Role == Role.Seller && !matched.IsShiftActiveNow())
        {
            _logger.LogWarning("Login blocked — shift inactive for seller {Username}", matched.Username);
            throw new InvalidOperationException(
                "Ish smenangiz hozir faol emas. Administrator bilan bog'laning.");
        }

        _logger.LogInformation("Login successful for user: {Username}, ID: {UserId}", matched.Username, matched.Id);
        // Wipe the brute-force counter — a user who got the password right
        // after one typo shouldn't stay one failure closer to a lockout for
        // the next 15 minutes.
        await _loginAttempts.RecordSuccessAsync(request.Username, cancellationToken);
        await _auditLogService.LogActionAsync(
            AuditEntityTypes.Auth, matched.Id, AuditActions.Login, matched.Id,
            new { username = matched.Username, role = matched.Role.ToString() }, cancellationToken);
        await RecordLoginHistoryAsync(matched.Id, true, cancellationToken);
        // Last-seen stamp (Сотрудники "в системе"). GenerateAuthResponseAsync
        // saves changes, so this persists with the refresh-token write.
        matched.LastActiveAt = DateTime.UtcNow;
        return await GenerateAuthResponseAsync(matched, cancellationToken);
    }

    /// <summary>
    /// Records a sign-in attempt for the Account "Последние входы" history.
    /// Best-effort — device/IP come from the current request; failures here
    /// never break the login flow.
    /// </summary>
    private async Task RecordLoginHistoryAsync(Guid userId, bool success, CancellationToken cancellationToken)
    {
        try
        {
            var http = _httpContext.HttpContext;
            _context.LoginHistories.Add(new LoginHistory
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Success = success,
                DeviceInfo = DescribeDevice(http?.Request.Headers.UserAgent.ToString()),
                IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record login history for {UserId}", userId);
        }
    }

    /// <summary>
    /// Throws when a market's subscription "door" is shut — blocked (423) or
    /// expired (402). Both are tenant-level states already exposed by the
    /// public market-state endpoint, so it is safe to signal them independently
    /// of (and before) credential verification. Blocked takes precedence over
    /// expired so a market that is both surfaces as "blocked".
    /// </summary>
    private void EnsureMarketOpen(Market market)
    {
        // O'CHIRILGAN do'kon — eshik butunlay yopiq.
        //
        // Slug bilan kirishda bu parol tekshiruvidan OLDIN qaralardi
        // (yuqorida), slugsiz yo'lda esa UMUMAN qaralmasdi: bu tekshiruv
        // shu metodda yo'q edi. Natijada o'chirilgan do'konning xodimi
        // ildizdagi `/login` orqali (slugsiz) bemalol kirar va do'kon
        // ma'lumotiga to'liq kirish olardi. Ikkala yo'l endi bitta
        // qoidaga tayanadi.
        if (!market.IsActive)
        {
            _logger.LogWarning("Login blocked — market {MarketId} is not active", market.Id);
            throw new Buildix.Domain.Exceptions.MarketBlockedException(
                market.Id, market.BlockedReason, market.BlockedAt);
        }

        if (market.IsBlocked)
        {
            _logger.LogWarning(
                "Login blocked — market {MarketId} is blocked. Reason={Reason}",
                market.Id, market.BlockedReason);
            throw new Buildix.Domain.Exceptions.MarketBlockedException(
                market.Id, market.BlockedReason, market.BlockedAt);
        }

        // Muddat o'tgani KIRISHNI darhol yopmaydi: otsrochka (Overdue) va
        // «faqat ko'rish» (Restricted) bosqichlarida foydalanuvchi kirishi
        // SHART — u to'lov haqidagi ogohlantirishni ko'rishi va ma'lumotini
        // o'qishi kerak. Eshik faqat to'liq blok bosqichida yopiladi.
        var settings = _platformSettings.Current;
        var state = market.EvaluateSubscription(
            DateTime.UtcNow, settings.GraceDays, settings.FullBlockAfterDays);
        if (state == SubscriptionState.Blocked)
        {
            _logger.LogWarning(
                "Login blocked — market {MarketId} subscription expired at {ExpiresAt:O}.",
                market.Id, market.ExpiresAt);
            throw new Buildix.Domain.Exceptions.SubscriptionExpiredException(
                market.Id, market.ExpiresAt);
        }
    }
}
