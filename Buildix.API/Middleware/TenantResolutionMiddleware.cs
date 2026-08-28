using System.Security.Claims;
using Buildix.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Buildix.Infrastructure.Data;

namespace Buildix.API.Middleware;

/// <summary>
/// </summary>
public class TenantResolutionMiddleware
{
    /// <summary>
    /// Obuna bosqichi shu kalit ostida <c>HttpContext.Items</c> ga qo'yiladi.
    /// <c>[RequiresActiveSubscription]</c> uni shu yerdan o'qiydi — market
    /// qatorini ikkinchi marta so'ramaslik uchun.
    /// </summary>
    public const string SubscriptionStateKey = "SubscriptionState";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext dbContext,
        IPlatformSettingsProvider platformSettings,
        Buildix.Application.Interfaces.ISubscriptionClock subscriptionClock)
    {
        // Skip tenant resolution for endpoints that don't operate on a single
        // tenant. The SuperAdmin console (`/api/_sa/...`) is cross-tenant by
        // design — its JWT has no MarketId claim — and public-facing endpoints
        // (auth, registration submission, health, hubs auth handshake) run
        // without a tenant context too.
        var path = context.Request.Path.Value ?? "";
        var skipPaths = new[]
        {
            "/api/Auth/Login",
            "/api/Auth/Register",
            "/api/Auth/RefreshToken",
            "/api/Auth/Logout",
            "/api/_sa/",                    // SuperAdmin console — cross-tenant
            "/api/RegistrationRequests",    // Public submission — anonymous
            "/api/public/",                 // Public market-state — pre-auth, no tenant
            "/health",
            "/swagger",
            "/privacy",
            "/hubs",
        };

        if (skipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // If the user is not authenticated, let the normal auth pipeline handle it
        // (returns 401 from [Authorize] for protected endpoints, or proceeds for [AllowAnonymous]).
        // Otherwise we mask the real auth failure with our "Market topilmadi" message.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // SuperAdmin is cross-tenant: their JWT has no MarketId claim by design
        // (they manage all markets, not one). Let the request pass through without
        // tenant resolution — the controllers they're allowed to call either use
        // the SuperAdmin console path (/api/_sa/) already skipped above, or are
        // user-scoped operations like /Users/MyProfile that work off the JWT claims alone.
        var roleClaim = context.User?.FindFirst(ClaimTypes.Role)?.Value;
        if (roleClaim == "SuperAdmin")
        {
            await _next(context);
            return;
        }

        // Avval JWT token'dan MarketId claim ni olamiz (birinchi prioritet)
        var marketIdClaim = context.User?.FindFirst("MarketId")?.Value;

        _logger.LogInformation("TenantResolution: User={User}, MarketIdClaim={MarketIdClaim}",
            context.User?.Identity?.Name, marketIdClaim);

        if (!string.IsNullOrEmpty(marketIdClaim) && int.TryParse(marketIdClaim, out var tokenMarketId))
        {
            // Real-time tenant-door enforcement: even after a token is issued,
            // a SuperAdmin block OR a lapsed subscription must take effect on the
            // very next request — not whenever the 30-min access token expires.
            // One PK lookup per request (Markets.Id is the primary key). The
            // subscription rule lives on the entity so it can't drift from the
            // login path / public state endpoint.
            var market = await dbContext.Markets
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == tokenMarketId);

            if (market is { IsBlocked: true })
            {
                _logger.LogWarning(
                    "Request rejected — market {MarketId} is blocked. User={User} Path={Path}",
                    tokenMarketId, context.User?.Identity?.Name, context.Request.Path);

                // 423 Locked is the canonical status for "resource exists but
                // is intentionally inaccessible". Body shape is the same as
                // any other API error so the Flutter client's global error
                // mapper can pick out `code` and route to a block screen.
                context.Response.StatusCode = 423;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "MARKET_BLOCKED",
                    message = "Do'kon administrator tomonidan bloklangan. Iltimos, administrator bilan bog'laning.",
                    reason = market.BlockedReason,
                    blockedAt = market.BlockedAt,
                    statusCode = 423
                });
                return;
            }

            // Obuna bosqichi — YAGONA manba (Market.EvaluateSubscription).
            // Muddat o'tgani darhol eshikni yopmaydi: otsrochka va «faqat
            // ko'rish» bosqichlarida so'rov o'tadi, holat esa header orqali
            // klientga beriladi (sariq plashka) va yozuv-amallar uchun
            // HttpContext'da qoladi ([RequiresActiveSubscription]).
            var subscriptionState = Domain.Enums.SubscriptionState.Active;
            if (market is not null)
            {
                var settings = platformSettings.Current;
                // Vaqt ATAYLAB `DateTime.UtcNow` dan emas. Do'kon dasturida
                // obuna muddati lokal nusxada turadi va u faqat bulutdan
                // tortilganda yangilanadi — internet uzilsa muzlab qoladi,
                // soat esa yuraveradi. Natijada TO'LAGAN do'kon otsrochka
                // tugagach savdo qila olmasdi, bir oydan keyin esa ilova
                // umuman ochilmasdi. Endi bulut jim bo'lgan vaqt
                // otsrochkani yemaydi (SubscriptionClock).
                var asOf = await subscriptionClock.NowAsync(context.RequestAborted);
                subscriptionState = market.EvaluateSubscription(
                    asOf, settings.GraceDays, settings.FullBlockAfterDays);
                context.Items[SubscriptionStateKey] = subscriptionState;
                if (subscriptionState != Domain.Enums.SubscriptionState.Active)
                    context.Response.Headers["X-Subscription-State"] = subscriptionState.ToString();
            }

            if (market is not null && subscriptionState == Domain.Enums.SubscriptionState.Blocked)
            {
                _logger.LogWarning(
                    "Request rejected — market {MarketId} subscription expired at {ExpiresAt:O}. User={User} Path={Path}",
                    tokenMarketId, market.ExpiresAt, context.User?.Identity?.Name, context.Request.Path);

                // 402 Payment Required — the subdomain's subscription lapsed.
                // Same body shape as MARKET_BLOCKED so the client's global error
                // mapper picks out `code` and routes to the renew screen.
                context.Response.StatusCode = 402;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "SUBSCRIPTION_EXPIRED",
                    message = "Obuna muddati tugagan. Iltimos, administrator bilan bog'lanib obunani yangilang.",
                    expiresAt = market.ExpiresAt,
                    statusCode = 402
                });
                return;
            }

            context.Items["MarketId"] = tokenMarketId;
            _logger.LogInformation("MarketId set from token: {MarketId}", tokenMarketId);
            await _next(context);
            return;
        }

        // DIQQAT: bu yerda ilgari subdomain (Host header) bo'yicha fallback bor edi —
        // qayta qo'shmang. Host header'ni to'liq klient boshqaradi, va u orqali topilgan
        // market uchun foydalanuvchi a'zoligi (membership) hech qachon tekshirilmagan edi:
        // MarketId claim'i yo'q token bilan istalgan tenant ichida ishlash mumkin bo'lardi
        // (Owner esa barcha permission tekshiruvlarini chetlab o'tadi). Tenant faqat
        // imzolangan JWT `MarketId` claim'idan olinadi; claim bo'lmasa — quyidagi 403.

        // MarketId topilmadi — user authenticated lekin market ga ruxsat yo'q → 403
        _logger.LogWarning("MarketId not found for user {User}, path {Path}",
            context.User?.Identity?.Name, context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Forbidden",
            message = "Market topilmadi. Iltimos, tizimga qaytadan kiring yoki administrator bilan bog'laning."
        });
    }
}
