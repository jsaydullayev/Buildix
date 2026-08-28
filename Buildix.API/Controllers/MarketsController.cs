using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Buildix.Application.Common;
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

    /// <summary>
    /// Ekrandagi ma'lumot qanchalik yangi ekani.
    ///
    /// <para>Egasi telefonda ko'radigan raqamlar do'kondan sinxronizatsiya
    /// orqali keladi. Do'kon internetsiz ishlayotgan bo'lsa, ular eskirgan
    /// bo'ladi — lekin eskirgandek KO'RINMAYDI. Bu yo'l shu farqni
    /// ko'rsatadi.</para>
    ///
    /// <para>Barcha rollarga ochiq: kassir ham o'z ekranidagi son qachongi
    /// ekanini bilishi kerak.</para>
    /// </summary>
    [HttpGet("~/api/Markets/sync-status")]
    [Authorize]
    public async Task<ActionResult<SyncFreshnessDto>> SyncStatus(
        [FromServices] ISyncFreshnessService freshness,
        CancellationToken cancellationToken)
        => Ok(await freshness.GetAsync(_currentMarketService.GetCurrentMarketId(), cancellationToken));

    /// <summary>
    /// Do'kon dasturini (desktop) yuklab olish manzili.
    ///
    /// <para><b>Nega alohida yo'l bilan beriladi.</b> O'rnatuvchi turgan
    /// papka nomi ataylab taxmin qilib bo'lmaydigan qilib qo'yilgan va
    /// nginx'da ro'yxat ko'rsatish o'chirilgan — ya'ni manzilni bilmasdan
    /// topib bo'lmaydi. Uni sahifaga qattiq yozib qo'ysak, o'sha sirning
    /// ma'nosi qolmasdi: manzil har bir tashrifchining brauzeriga
    /// tushardi. Shu yerda esa uni faqat kirgan EGA oladi.</para>
    ///
    /// <para>Sozlanmagan bo'lsa <c>url</c> bo'sh qaytadi — sahifa tugma
    /// o'rniga «hali tayyor emas» deb yozadi. Bu xato emas: yangi
    /// o'rnatilgan serverda paket hali qo'yilmagan bo'lishi normal.</para>
    ///
    /// <para><b>Versiya qo'lda yozilmaydi.</b> U o'rnatuvchi bilan yonma-yon
    /// yotgan <c>releases.win.json</c> dan o'qiladi — yangilanish mexanizmi
    /// ham aynan o'sha faylga qaraydi. Ilgari raqam <c>.env</c> da alohida
    /// turardi va papkadagi paket bilan ajralib qolishi mumkin edi: sahifa
    /// egaga bir raqamni ko'rsatar, yuklab olingan fayl esa boshqasi
    /// bo'lardi va bu hech qanday belgi bermasdi.</para>
    /// </summary>
    [HttpGet("~/api/Markets/desktop-app")]
    [Authorize(Policy = "OwnerOnly")]
    public ActionResult<DesktopAppDto> DesktopApp([FromServices] IConfiguration configuration)
    {
        var url = configuration["Desktop:InstallerUrl"]?.Trim() ?? string.Empty;
        return Ok(new DesktopAppDto(url, ResolveVersion(configuration, url)));
    }

    /// <summary>
    /// Chiqarilgan versiya. Fayl o'qilmasa — sozlamadagi qiymat.
    /// </summary>
    /// <remarks>
    /// Zaxira ATAYLAB qoldirilgan: paketlar boshqa yo'l bilan tarqatiladigan
    /// (yoki umuman ulanmagan) o'rnatmalarda fayl bo'lmasligi mumkin va
    /// o'shanda sahifa versiyasiz qolishi kerak emas.
    /// </remarks>
    private static string? ResolveVersion(IConfiguration configuration, string installerUrl)
    {
        var configured = configuration["Desktop:Version"]?.Trim();
        var folder = DesktopRelease.FolderFromUrl(installerUrl);
        if (folder is null) return configured;

        var root = configuration["Desktop:UpdatesRoot"]?.Trim();
        if (string.IsNullOrWhiteSpace(root)) return configured;

        try
        {
            var path = Path.Combine(root, folder, "releases.win.json");
            if (!System.IO.File.Exists(path)) return configured;
            return DesktopRelease.VersionFromReleases(System.IO.File.ReadAllText(path)) ?? configured;
        }
        catch (IOException)
        {
            return configured;
        }
        catch (UnauthorizedAccessException)
        {
            return configured;
        }
    }

    /// <summary>
    /// Kassaga kerak bo'ladigan chop etish sozlamalari.
    /// </summary>
    /// <remarks>
    /// <para>To'liq sozlamalar ekrani faqat EGAGA ochiq, lekin chek eni
    /// kassirga ham kerak: chekni u bosadi. Ilgari en interfeysga QATTIQ
    /// 80 deb yozilgan edi va 58 mm printerli do'konda chek qog'ozga
    /// sig'masdi — drayver uni o'zicha siqib bosardi, har bir harf
    /// alohida qatorga tushardi.</para>
    ///
    /// <para>Omma uchun ochiq yo'lga qo'yilmadi: do'kon sozlamasi kirgan
    /// xodimga tegishli, tashrifchiga emas.</para>
    /// </remarks>
    [HttpGet("~/api/Markets/pos-settings")]
    [Authorize]
    public async Task<ActionResult<PosPrintSettingsDto>> PosSettings(CancellationToken cancellationToken)
    {
        var settings = await _marketSettingsService.GetOrCreateAsync(
            _currentMarketService.GetCurrentMarketId(), cancellationToken);
        return Ok(new PosPrintSettingsDto(settings.ReceiptWidthMm, settings.AutoPrintReceipt));
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
