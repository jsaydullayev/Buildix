using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Buildix.API.Filters;

/// <summary>
/// Marks a money-moving action as idempotent. When the client sends an
/// <c>Idempotency-Key</c> header, a retried request (double-click, mobile retry,
/// proxy replay) is de-duplicated: the first call runs and its 2xx response is
/// stored; a duplicate replays that response (or is told to retry / rejected on
/// payload mismatch) instead of moving money twice. No header → the action runs
/// exactly as before (opt-in, additive).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : TypeFilterAttribute
{
    /// <param name="scope">Operation scope, e.g. "sale-payment".</param>
    /// <param name="marketRouteKey">
    /// Route parameter holding the target market id. Needed by the SuperAdmin
    /// console: its caller has NO MarketId claim, so the usual
    /// <c>ICurrentMarketService</c> lookup throws and the guard would silently
    /// skip de-duplication — exactly on the endpoints that move money. Naming
    /// the route value keeps the key scoped to the market being paid for.
    /// </param>
    public IdempotentAttribute(string scope, string? marketRouteKey = null)
        : base(typeof(IdempotencyFilter))
    {
        // MAJBURIY: bo'sh satr, `null` EMAS. TypeFilterAttribute filtr nusxasini
        // yaratishda `Arguments.Select(a => a.GetType())` qiladi — massivdagi
        // `null` o'sha yerda NullReferenceException beradi va endpoint HAR
        // chaqiruvda 500 qaytaradi (kalit yuborilgan-yuborilmaganidan qat'i
        // nazar, chunki filtr hatto qurilmaydi). Filtr bo'sh satrni "yo'q" deb
        // o'qiydi.
        Arguments = new object[] { scope, marketRouteKey ?? string.Empty };
    }
}

/// <summary>The filter behind <see cref="IdempotentAttribute"/>.</summary>
public sealed class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 200;

    // Replay must be byte-identical to what MVC originally wrote, so the body
    // is serialised with the APPLICATION's configured options — not a private
    // copy. A private copy silently drifted: it lacked TashkentTimeJsonConverter,
    // so the first response carried local time and its replay carried raw UTC.
    // Same instant, different text — exactly what "idempotent" promises not to do.
    private readonly JsonSerializerOptions _jsonOpts;

    // Payload hashing is a DIFFERENT job: it only has to be stable for the
    // lifetime of a key, and it must not shift when the app's response
    // formatting changes. Hence its own fixed options.
    private static readonly JsonSerializerOptions HashOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _scope;
    private readonly string? _marketRouteKey;
    private readonly IIdempotencyService _idempotency;
    private readonly ICurrentMarketService _market;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(
        string scope,
        string? marketRouteKey,
        IIdempotencyService idempotency,
        ICurrentMarketService market,
        IOptions<JsonOptions> jsonOptions,
        ILogger<IdempotencyFilter> logger)
    {
        _scope = scope;
        _marketRouteKey = string.IsNullOrEmpty(marketRouteKey) ? null : marketRouteKey;
        _idempotency = idempotency;
        _market = market;
        _jsonOpts = jsonOptions.Value.JsonSerializerOptions;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var key = context.HttpContext.Request.Headers[HeaderName].ToString();

        // Opt-in: no key → behave exactly as if the filter weren't here.
        if (string.IsNullOrWhiteSpace(key))
        {
            await next();
            return;
        }

        if (key.Length > MaxKeyLength)
        {
            context.Result = new ObjectResult(new { message = $"Idempotency-Key too long (max {MaxKeyLength})." })
            { StatusCode = StatusCodes.Status400BadRequest };
            return;
        }

        // A market scope is required to store the key. The console names its
        // route parameter (the caller has no MarketId claim); everything else
        // reads the tenant claim. If neither yields a market, let the normal
        // auth/tenant pipeline reject the request.
        int marketId;
        if (_marketRouteKey is not null
            && context.RouteData.Values.TryGetValue(_marketRouteKey, out var raw)
            && int.TryParse(raw?.ToString(), out var fromRoute))
        {
            marketId = fromRoute;
        }
        else
        {
            try { marketId = _market.GetCurrentMarketId(); }
            catch (UnauthorizedAccessException) { await next(); return; }
        }

        var requestHash = ComputeHash(context.ActionArguments);
        var ct = context.HttpContext.RequestAborted;

        var begin = await _idempotency.BeginAsync(_scope, key, marketId, requestHash, ct);
        switch (begin.Decision)
        {
            case IdempotencyDecision.Replay:
                _logger.LogInformation("Idempotent replay: scope={Scope} key={Key}", _scope, key);
                context.Result = new ContentResult
                {
                    StatusCode = begin.StatusCode,
                    Content = begin.Body,
                    ContentType = "application/json",
                };
                return;

            case IdempotencyDecision.InProgress:
                context.Result = new ObjectResult(new { message = "Duplicate request already in progress." })
                { StatusCode = StatusCodes.Status409Conflict };
                return;

            case IdempotencyDecision.PayloadMismatch:
                context.Result = new ObjectResult(new { message = "Idempotency-Key reused with a different request." })
                { StatusCode = StatusCodes.Status422UnprocessableEntity };
                return;
        }

        // Proceed: run the action, then persist its outcome under the key.
        var executed = await next();
        var (status, body) = ExtractResponse(executed);
        await _idempotency.CompleteAsync(_scope, key, marketId, status, body, ct);
    }

    private (int status, string? body) ExtractResponse(ActionExecutedContext executed)
    {
        // An unhandled exception (→ 500) or a non-2xx result releases the claim.
        if (executed.Exception is not null && !executed.ExceptionHandled)
            return (StatusCodes.Status500InternalServerError, null);

        return executed.Result switch
        {
            ObjectResult obj => (obj.StatusCode ?? StatusCodes.Status200OK, Serialize(obj.Value)),
            JsonResult jr => (jr.StatusCode ?? StatusCodes.Status200OK, Serialize(jr.Value)),
            ContentResult cr => (cr.StatusCode ?? StatusCodes.Status200OK, cr.Content),
            StatusCodeResult sc => (sc.StatusCode, null),
            _ => (StatusCodes.Status200OK, null),
        };
    }

    private string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, _jsonOpts);

    /// <summary>
    /// SHA-256 over the bound action arguments (minus the CancellationToken) so a
    /// key reused with a different payload can be detected. Best-effort: if the
    /// args can't be serialised, returns "" and mismatch detection is skipped —
    /// idempotency (dedup/replay) still works.
    /// </summary>
    private static string ComputeHash(IDictionary<string, object?> args)
    {
        try
        {
            var relevant = args
                .Where(kv => kv.Value is not CancellationToken)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            var json = JsonSerializer.Serialize(relevant, HashOpts);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }
        catch
        {
            return string.Empty;
        }
    }
}
