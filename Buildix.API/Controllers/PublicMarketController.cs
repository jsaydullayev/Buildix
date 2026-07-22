using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Buildix.API.Controllers;

/// <summary>
/// Public, unauthenticated entry point for the per-market login page
/// (buildix.uz/{subdomain}/login). Exposes ONLY the market's login-door state
/// so the SPA can decide whether to render the login form ("active") or a
/// blocked / expired notice. No user, session, or business data is returned.
///
/// The route lives under <c>/api/public/</c>, which TenantResolutionMiddleware
/// skips (no tenant context on a pre-auth call).
/// </summary>
[ApiController]
[Route("api/public/market")]
public class PublicMarketController : ControllerBase
{
    private readonly IMarketService _marketService;

    public PublicMarketController(IMarketService marketService)
    {
        _marketService = marketService;
    }

    /// <summary>
    /// GET /api/public/market/{subdomain} — market-door state by slug.
    /// 200 with { subdomain, marketName, state, expiresAt } when the market
    /// exists and is active; 404 when the slug is unknown or soft-deleted.
    /// </summary>
    [HttpGet("{subdomain}")]
    [AllowAnonymous]
    [EnableRateLimiting("public-market")]
    public async Task<ActionResult<PublicMarketStateDto>> GetState(string subdomain, CancellationToken cancellationToken)
    {
        var state = await _marketService.GetPublicStateBySubdomainAsync(subdomain, cancellationToken);
        if (state is null)
            return NotFound(new { message = "Do'kon topilmadi." });
        return Ok(state);
    }
}
