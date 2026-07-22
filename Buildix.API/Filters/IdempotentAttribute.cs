using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
    public IdempotentAttribute(string scope) : base(typeof(IdempotencyFilter))
    {
        Arguments = new object[] { scope };
    }
}

/// <summary>The filter behind <see cref="IdempotentAttribute"/>.</summary>
public sealed class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 200;

    // Match the API's global JSON shape (camelCase, string enums) so a replayed
    // body is byte-identical to what the action originally returned.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _scope;
    private readonly IIdempotencyService _idempotency;
    private readonly ICurrentMarketService _market;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(
        string scope,
        IIdempotencyService idempotency,
        ICurrentMarketService market,
        ILogger<IdempotencyFilter> logger)
    {
        _scope = scope;
        _idempotency = idempotency;
        _market = market;
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

        // A market scope is required to store the key. If there's none (missing
        // claim), let the normal auth/tenant pipeline reject the request.
        int marketId;
        try { marketId = _market.GetCurrentMarketId(); }
        catch (UnauthorizedAccessException) { await next(); return; }

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

    private static (int status, string? body) ExtractResponse(ActionExecutedContext executed)
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

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOpts);

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
            var json = JsonSerializer.Serialize(relevant, JsonOpts);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }
        catch
        {
            return string.Empty;
        }
    }
}
