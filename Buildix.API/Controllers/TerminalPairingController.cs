using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Buildix.API.Controllers;

/// <summary>
/// Do'kon kompyuterini bulutga bog'lash.
///
/// <para><b>Bu yagona ANONIM sinxronizatsiya yo'li.</b> Yangi o'rnatilgan
/// ilovada hali hech qanday kalit yo'q, ya'ni u o'zini tanitib bo'lmaydi —
/// aynan shuning uchun kod kerak. Qolgan hamma sinxronizatsiya yo'llari
/// kalit talab qiladi.</para>
/// </summary>
[ApiController]
[Route("api/pairing")]
public class TerminalPairingController : ControllerBase
{
    private readonly ITerminalPairingService _pairing;

    public TerminalPairingController(ITerminalPairingService pairing) => _pairing = pairing;

    /// <summary>
    /// Kodni kalitga almashtiradi. Do'kon ilovasi o'rnatish kunida bir marta
    /// chaqiradi.
    ///
    /// <para><b>Tezlik qattiq cheklangan.</b> Kod sakkiz belgi va uni
    /// tanlab olishga urinish mumkin. Boshqa himoya yo'q — endpoint anonim,
    /// shuning uchun cheklov shu yerda hal qiluvchi ahamiyatga ega.</para>
    /// </summary>
    [HttpPost("redeem")]
    [AllowAnonymous]
    [EnableRateLimiting("terminal-pairing")]
    public async Task<ActionResult<PairedTerminalDto>> Redeem(
        [FromBody] RedeemPairingRequest request, CancellationToken ct = default)
    {
        var result = await _pairing.RedeemAsync(
            request.Code, request.TerminalName, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        // Xato turi ATAYLAB ajratilmaydi: har qanday muvaffaqiyatsizlik uchun
        // bitta javob va bitta matn. «Bu kod bor, lekin muddati o'tgan» degan
        // javob taxmin qiluvchiga qaysi kodlar mavjudligini aytib berardi.
        if (result.IsFailure) return BadRequest(new { message = result.Error });

        return Ok(result.Value);
    }
}
