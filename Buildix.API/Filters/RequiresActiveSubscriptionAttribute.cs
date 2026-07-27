using Buildix.API.Middleware;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Buildix.API.Filters;

/// <summary>
/// Pul harakatlantiradigan amalni obunaga bog'laydi.
///
/// <para><b>Nima uchun nuqtali (opt-in).</b> Dizayndagi «Режим только
/// просмотр» ning o'z ta'rifi — <i>«продажи заблокированы, данные видны»</i>.
/// Ya'ni butun yozuvni (har qanday POST/PUT/DELETE) taqiqlash talab
/// qilinmagan va zararli bo'lardi: kassir yarim qolgan smenani yopa olmasdi,
/// mijozdan qarz to'lovini qabul qila olmasdi, hatto chiqib keta olmasdi.
/// Shu sababli atribut FAQAT tushum keltiradigan yozuvlarga qo'yiladi —
/// qo'yilmagan endpoint xatti-harakati umuman o'zgarmaydi.</para>
///
/// <para>Holatni <see cref="TenantResolutionMiddleware"/> allaqachon
/// hisoblagan va <c>HttpContext.Items</c> ga qo'ygan — bu yerda DB'ga
/// qayta murojaat yo'q.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresActiveSubscriptionAttribute : TypeFilterAttribute
{
    public RequiresActiveSubscriptionAttribute() : base(typeof(SubscriptionWriteGate)) { }
}

/// <summary>The filter behind <see cref="RequiresActiveSubscriptionAttribute"/>.</summary>
public sealed class SubscriptionWriteGate : IActionFilter
{
    private readonly IPlatformSettingsProvider _settings;
    private readonly ILogger<SubscriptionWriteGate> _logger;

    public SubscriptionWriteGate(IPlatformSettingsProvider settings, ILogger<SubscriptionWriteGate> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Middleware holatni qo'ymagan bo'lsa (SuperAdmin, anonim yo'l) —
        // cheklov ham yo'q: bu atribut faqat tenant so'rovlari uchun.
        if (context.HttpContext.Items[TenantResolutionMiddleware.SubscriptionStateKey]
            is not SubscriptionState state)
            return;

        // «Faqat ko'rish» rejimi o'chirilgan bo'lsa, do'kon to'liq blok
        // kunigacha odatdagidek ishlaydi (Настройки → «Режим только просмотр»).
        if (state != SubscriptionState.Restricted || !_settings.Current.RestrictAfterGrace)
            return;

        _logger.LogWarning(
            "Write blocked — subscription restricted. User={User} Path={Path}",
            context.HttpContext.User?.Identity?.Name, context.HttpContext.Request.Path);

        context.Result = new ObjectResult(new
        {
            code = "SUBSCRIPTION_RESTRICTED",
            message = "Obuna muddati tugagan — sotuv vaqtincha to'xtatilgan. "
                      + "Ma'lumotlar ochiq. Obunani yangilash uchun administrator bilan bog'laning.",
            statusCode = StatusCodes.Status402PaymentRequired,
        })
        { StatusCode = StatusCodes.Status402PaymentRequired };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
