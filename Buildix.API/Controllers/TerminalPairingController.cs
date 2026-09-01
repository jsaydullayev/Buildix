using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
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
    private readonly IAuthService _auth;

    public TerminalPairingController(ITerminalPairingService pairing, IAuthService auth)
    {
        _pairing = pairing;
        _auth = auth;
    }

    /// <summary>
    /// Do'kon egasining login-paroli bilan bog'laydi — ASOSIY yo'l.
    ///
    /// <para><b>Nega kodsiz.</b> Kodni faqat SuperAdmin bera olardi, ya'ni
    /// egasi yangi kassani o'zi ishga tushira olmasdi. Endi u desktopni
    /// yuklab olib, o'sha hisobi bilan kiradi — boshqa hech narsa
    /// kerak emas.</para>
    ///
    /// <para><b>Faqat Owner.</b> Kassir ham bog'lay olsa, o'g'irlangan
    /// bitta parol butun do'kon bazasini begona kompyuterga ko'chirish
    /// imkonini berardi. Kassir odatdagidek ishlaydi, lekin YANGI
    /// kompyuterni faollashtira olmaydi.</para>
    ///
    /// <para>Parol tekshiruvi <see cref="IAuthService"/> orqali o'tadi,
    /// ya'ni qulflash (brute-force) himoyasi ham shu yerda ishlaydi.</para>
    /// </summary>
    [HttpPost("activate")]
    [AllowAnonymous]
    [EnableRateLimiting("terminal-pairing")]
    public async Task<ActionResult<PairedTerminalDto>> Activate(
        [FromBody] ActivateTerminalRequest request, CancellationToken ct = default)
    {
        // Xato matni ATAYLAB bitta: «bunday login yo'q» va «parol xato»
        // ni ajratish begona odamga mavjud loginlarni sanab berardi.
        const string Rejected = "Login yoki parol noto'g'ri.";

        AuthResponse? auth;
        try
        {
            auth = await _auth.LoginAsync(
                new LoginRequest(request.Username, request.Password, request.Subdomain), ct);
        }
        catch (Buildix.Domain.Exceptions.LoginLockedException ex)
        {
            // Qulflanganini YASHIRMAYMIZ: egasi parolini eslay olmayotgan
            // bo'lsa, «yana urinib ko'ring» degan xabar uni cheksiz
            // urinishdan saqlaydi.
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
        }

        if (auth is null) return BadRequest(new { message = Rejected });

        if (!string.Equals(auth.Role, nameof(Role.Owner), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new
            {
                message = "Yangi kompyuterni faqat do'kon EGASI faollashtira oladi. "
                        + "Egasining login-paroli bilan kiring yoki paneldan bir martalik kod oling.",
            });

        if (auth.MarketId is not { } marketId)
            return BadRequest(new { message = "Bu hisob hech qaysi do'konga bog'lanmagan." });

        // `ReplaceExisting` ni EGA tasdiqlaydi: bu yo'lga faqat uning
        // login-paroli bilan kelinadi va yuqorida rol tekshirilgan.
        var result = await _pairing.ActivateAsync(
            marketId, request.TerminalName, HttpContext.Connection.RemoteIpAddress?.ToString(),
            request.ReplaceExisting, ct);

        // Xato KODI ham qaytariladi: ilova «eskisini bekor qilib bog'lash»
        // bandini aynan shunga qarab ko'rsatadi. Matn bo'yicha tanish
        // mo'rt bo'lardi — xabar tahrirlansa band jimgina yo'qolardi.
        // Bu yo'l egaga tanitilgan, ya'ni kod hech narsani oshkor qilmaydi.
        if (result.IsFailure)
            return BadRequest(new { message = result.Error, code = result.Code });

        return Ok(result.Value);
    }

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
