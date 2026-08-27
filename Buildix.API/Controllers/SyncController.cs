using Buildix.API.Filters;
using Buildix.Application.Common;
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

    private readonly IAppDbContext _db;

    public SyncController(ISyncPullService pull, IAppDbContext db)
    {
        _pull = pull;
        _db = db;
    }

    /// <summary>
    /// Do'kondan kelgan yozuvlarni qabul qiladi.
    ///
    /// <para>Kelgan qatorlarning <c>MarketId</c> si kalitdan aniqlangan
    /// do'konga majburan almashtiriladi — do'kon qaysi do'kon ekanini o'zi
    /// aytmaydi.</para>
    /// </summary>
    [HttpPost("push")]
    public async Task<ActionResult<SyncPushResultDto>> Push(
        [FromServices] ISyncPushService push,
        CancellationToken ct = default)
    {
        var terminal = (ShopTerminal)HttpContext.Items[TerminalAuthorizeAttribute.TerminalItemKey]!;

        // Tana ATAYLAB qo'lda o'qiladi. `[FromBody]` bo'lsa, MVC uni global
        // sozlama bilan o'qir edi — u yerda esa vaqtni Toshkent mintaqasiga
        // suradigan o'zgartirgich turibdi. Sinxronizatsiya kanali undan
        // butunlay mustaqil bo'lishi SHART: bir marta shu tuzoqqa tushilgan
        // va natijada sanalar 5 soatga siljigan edi.
        SyncPushDto? payload;
        try
        {
            payload = await System.Text.Json.JsonSerializer.DeserializeAsync<SyncPushDto>(
                Request.Body, EntityWireFormat.Options, ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest(new { message = "To'plamni o'qib bo'lmadi: " + ex.Message });
        }

        if (payload is null) return BadRequest(new { message = "Bo'sh to'plam." });

        var result = await push.AcceptAsync(terminal.MarketId, payload, ct);

        // Ma'lumot HAQIQATAN yozildi — endi «yangilik» belgisini shu yerda
        // qo'yamiz.
        //
        // Ilgari yangilik `LastSeenAtUtc` ga qarardi, u esa kalit
        // tekshiruvidan o'tishda qo'yilardi: push tashqi kalit xatosi bilan
        // yiqilsa ham «do'kon hozirgina aloqada bo'ldi» deb belgilanardi.
        // Natijada egasining telefonida yashil «sinxron» yozuvi turar, bulutga
        // esa haftalab birorta savdo tushmasdi. Aloqa boshqa narsa,
        // ma'lumotning yetib kelishi boshqa.
        await MarkDataReceivedAsync(terminal, ct);

        return Ok(result);
    }

    /// <summary>
    /// Do'kondan ma'lumot kelgan vaqtni belgilaydi. Daqiqada bir marta —
    /// har push'da yozish jadvalni bekorga bezovta qilardi.
    /// </summary>
    private async Task MarkDataReceivedAsync(ShopTerminal terminal, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (terminal.LastPushAtUtc is { } last && now - last < TimeSpan.FromMinutes(1)) return;

        try
        {
            terminal.LastPushAtUtc = now;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception)
        {
            // Belgini yozib bo'lmasa ham ma'lumot allaqachon qabul qilingan —
            // bu chaqiruvni yiqitish qabul qilingan qatorlarni qayta
            // yuborishga majbur qilardi.
        }
    }

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
