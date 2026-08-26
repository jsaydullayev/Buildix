using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Buildix.API.Filters;

/// <summary>
/// So'rovni do'kon kompyuterining kaliti bo'yicha taniydi.
///
/// <para><b>Nega odam tokeni emas.</b> Sinxronizatsiya kassir chiqib
/// ketgandan keyin ham, kechasi ham ishlashi kerak — ya'ni uni hech kimning
/// seansiga bog'lab bo'lmaydi. Kalit kompyuterga tegishli va SuperAdmin
/// panelidan bekor qilinadi.</para>
///
/// <para><b>Market konteksti shu yerda qo'yiladi.</b>
/// <c>HttpContext.Items["MarketId"]</c> — <c>CurrentMarketService</c> aynan
/// shuni o'qiydi, ya'ni undan keyingi HAR BIR so'rov global tenant filtridan
/// o'tadi. Buni qo'ymaslik xizmatlarni market kontekstisiz qoldirar va ular
/// barcha do'konlarning ma'lumotini ko'radigan bo'lib qolardi.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TerminalAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Terminal-Key";

    /// <summary>Tanilgan kompyuter shu kalit ostida qoladi.</summary>
    public const string TerminalItemKey = "ShopTerminal";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();

        var pairing = http.RequestServices.GetRequiredService<ITerminalPairingService>();
        var terminal = await pairing.AuthenticateAsync(key, http.RequestAborted);

        if (terminal is null)
        {
            // Sabab aytilmaydi: «kalit yo'q», «noto'g'ri» va «bekor qilingan»
            // uchun bitta javob.
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "Kompyuter tanilmadi. Uni bulutga qaytadan bog'lash kerak.",
            });
            return;
        }

        http.Items["MarketId"] = terminal.MarketId;
        http.Items[TerminalItemKey] = terminal;

        // Aloqa vaqti shu yerda belgilanadi. Busiz maydon faqat bog'langan
        // kunni ko'rsatib turardi va «do'kon uch kundan beri aloqaga
        // chiqmayapti» degan xabar uchun ma'lumot umuman bo'lmasdi.
        await pairing.TouchAsync(
            terminal, http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted);

        await next();
    }
}
