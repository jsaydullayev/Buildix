using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Interfaces;
using System.Security.Claims;

namespace Buildix.API.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class MarketsController : ApiControllerBase
{
    private readonly IMarketService _marketService;
    private readonly ILogger<MarketsController> _logger;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IMarketSettingsService _marketSettingsService;

    public MarketsController(
        IMarketService marketService,
        ILogger<MarketsController> logger,
        ICurrentMarketService currentMarketService,
        IMarketSettingsService marketSettingsService)
    {
        _marketService = marketService;
        _logger = logger;
        _currentMarketService = currentMarketService;
        _marketSettingsService = marketSettingsService;
    }

    // Owner only — the whole Настройки screen. Absolute routes so the paths
    // stay clean (GET/PUT /api/Markets/settings) under the [action] template.
    [HttpGet("~/api/Markets/settings")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<ActionResult<MarketSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _marketSettingsService.GetAsync(cancellationToken);
        return Ok(settings);
    }

    [HttpPut("~/api/Markets/settings")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<ActionResult<MarketSettingsDto>> UpdateSettings(
        [FromBody] UpdateMarketSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _marketSettingsService.UpdateAsync(request, cancellationToken);
        return Ok(settings);
    }

    // SuperAdmin only - Create market with new Owner user
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> CreateMarket([FromBody] CreateMarketRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _marketService.CreateMarketAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Owner only - Register market for themselves (updates their existing account)
    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> RegisterMarket([FromBody] RegisterMarketRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out var ownerId))
                return Unauthorized();

            // The service registers the market, links the owner, and mints a fresh
            // JWT carrying the new MarketId — the reload + token wiring that used to
            // live here now lives in the service (which already holds the owner).
            var result = await _marketService.RegisterMarketForOwnerAsync(request, ownerId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Owner only - Get their own market details
    [HttpGet]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetMyMarket(CancellationToken cancellationToken)
    {
        try
        {
            var marketId = _currentMarketService.TryGetCurrentMarketId();
            if (!marketId.HasValue)
                return NotFound(new { message = "Sizga tegishli market topilmadi" });

            var market = await _marketService.GetMarketByIdAsync(marketId.Value, cancellationToken);
            if (market is null)
                return NotFound(new { message = "Market topilmadi" });

            return Ok(market);
        }
        catch (Exception ex) when (NotHandledGlobally(ex))
        {
            _logger.LogError(ex, "Error getting market for owner");
            return StatusCode(500, new { message = "Xatolik yuz berdi" });
        }
    }

    // Owner only - Update their own market details
    [HttpPut]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> UpdateMyMarket([FromBody] UpdateMyMarketRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var marketId = _currentMarketService.TryGetCurrentMarketId();
            if (!marketId.HasValue)
                return NotFound(new { message = "Sizga tegishli market topilmadi" });

            var result = await _marketService.UpdateMarketAsync(marketId.Value, request.Name, request.Description, cancellationToken);
            if (!result)
                return NotFound(new { message = "Market topilmadi" });

            return Ok(new { message = "Market ma'lumotlari muvaffaqiyatli yangilandi" });
        }
        catch (Exception ex) when (NotHandledGlobally(ex))
        {
            _logger.LogError(ex, "Error updating market for owner");
            return StatusCode(500, new { message = "Xatolik yuz berdi" });
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetAllMarkets(CancellationToken cancellationToken)
    {
        var markets = await _marketService.GetAllMarketsAsync(cancellationToken);
        return Ok(markets);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetMarketById(int id, CancellationToken cancellationToken)
    {
        var market = await _marketService.GetMarketByIdAsync(id, cancellationToken);
        if (market is null) return NotFound();
        return Ok(market);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> UpdateMarket(int id, [FromBody] UpdateMarketRequest request, CancellationToken cancellationToken)
    {
        var result = await _marketService.UpdateMarketAsync(id, request.Name, request.Description, cancellationToken);
        if (!result) return NotFound();
        return Ok(new { message = "Market updated successfully" });
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DeleteMarket(int id, CancellationToken cancellationToken)
    {
        var result = await _marketService.DeleteMarketAsync(id, cancellationToken);
        if (!result) return NotFound();
        return Ok(new { message = "Market deleted successfully" });
    }
}
