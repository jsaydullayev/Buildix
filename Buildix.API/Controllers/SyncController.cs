using Buildix.API.Filters;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buildix.API.Controllers;

/// <summary>
/// Do'kon kompyuteri bilan bulut o'rtasidagi kanal.
///
/// <para>Barcha yo'llar KALIT bilan ishlaydi (<see cref="TerminalAuthorizeAttribute"/>),
/// odam tokeni bilan emas: sinxronizatsiya kassir chiqib ketgandan keyin ham
/// ishlashi kerak.</para>
/// </summary>
[ApiController]
[Route("api/sync")]
[AllowAnonymous]              // odam tokeni talab qilinmaydi...
[TerminalAuthorize]           // ...lekin kompyuter kaliti SHART
public class SyncController : ControllerBase
{
    private readonly ISyncPullService _pull;

    public SyncController(ISyncPullService pull) => _pull = pull;

    /// <summary>
    /// Bulutdagi o'zgarishlarni beradi: do'konning o'zi va xodimlar.
    ///
    /// <para><paramref name="since"/> berilmasa hammasi qaytadi — yangi
    /// o'rnatilgan do'kon aynan shunday boshlanadi.</para>
    /// </summary>
    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullDto>> Pull(
        [FromQuery] DateTimeOffset? since, CancellationToken ct = default)
    {
        var terminal = (ShopTerminal)HttpContext.Items[TerminalAuthorizeAttribute.TerminalItemKey]!;

        // DateTimeOffset.MinValue emas, aniq sana: Npgsql eng kichik qiymatni
        // '-infinity' ga aylantiradi va u bilan solishtirish kutilgandek
        // ishlamaydi.
        var from = since ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return Ok(await _pull.PullAsync(terminal.MarketId, from, ct));
    }
}
