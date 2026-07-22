using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Buildix.Domain.Constants;
using Buildix.Domain.Exceptions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buildix.API.Middleware;

/// <summary>
/// Global exception handler to catch and format all unhandled exceptions.
/// In production only safe, generic messages are returned to the client.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception. TraceId={TraceId}", context.TraceIdentifier);
            await HandleExceptionAsync(context, ex);
            await TryRecordErrorAsync(context, ex);
        }
    }

    /// Record a server-side fault (5xx) into the audit log so it surfaces in the
    /// security journal's "Suspicious" tab with a status code + message the
    /// Owner/developer can act on. Best-effort — a failure here (e.g. the DB is
    /// the thing that's down) must never break the error response we just wrote.
    private static async Task TryRecordErrorAsync(HttpContext context, Exception ex)
    {
        try
        {
            if (context.Response.StatusCode < 500) return; // only real server faults
            var audit = context.RequestServices.GetService(typeof(IAuditLogService)) as IAuditLogService;
            if (audit is null) return;

            var userIdStr = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = Guid.TryParse(userIdStr, out var parsed) ? parsed : Guid.Empty;

            await audit.LogActionAsync(
                AuditEntityTypes.Error,
                Guid.NewGuid(),
                AuditActions.Error,
                userId,
                new
                {
                    StatusCode = context.Response.StatusCode,
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value,
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message
                });
        }
        catch
        {
            // Swallow — audit logging must not mask the original error.
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        var isDev = _env.IsDevelopment();
        var response = new ErrorResponse { TraceId = context.TraceIdentifier };

        switch (exception)
        {
            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Message = "Sizga bu amalni bajarishga ruxsat yo'q.";
                break;

            case DomainException domainEx:
                // Every expected domain-rule violation carries its own HTTP
                // status, machine-readable code, client-safe message and
                // (optionally) reason / blocked-at — defined on the exception
                // itself (Buildix.Domain.Exceptions). One arm handles them all;
                // adding a new domain exception needs no middleware edit.
                //   MARKET_BLOCKED → 423, ACCOUNT_LOCKED → 429,
                //   SHIFT_NOT_OPEN / USERNAME_TAKEN → 409,
                //   SUBSCRIPTION_EXPIRED → 402.
                context.Response.StatusCode = domainEx.StatusCode;
                response.Message = domainEx.UserMessage;
                response.Code = domainEx.Code;
                response.Reason = domainEx.Reason;
                response.BlockedAt = domainEx.BlockedAt;
                response.ExpiresAt = domainEx.ExpiresAt;
                break;

            case InvalidOperationException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = isDev ? exception.Message : "So'rov noto'g'ri. Iltimos, ma'lumotlarni tekshiring.";
                break;

            case ArgumentException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = isDev ? exception.Message : "Noto'g'ri parametr yuborildi.";
                break;

            case KeyNotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Message = "Ma'lumot topilmadi.";
                break;

            case DbUpdateConcurrencyException:
                // Surface optimistic-concurrency conflicts (e.g. stock updated by another sale).
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                response.Message = "Ma'lumot boshqa foydalanuvchi tomonidan o'zgartirildi. Iltimos, qaytadan urinib ko'ring.";
                break;

            case NotImplementedException:
                // Feature intentionally unimplemented. In dev, surface the raw message so
                // the developer can tell which stub fired. In prod, return a generic
                // string — a future contributor might toss connection strings or file
                // paths into a NotImplementedException message and we don't want them
                // leaking to clients.
                context.Response.StatusCode = (int)HttpStatusCode.NotImplemented; // 501
                response.Message = isDev
                    ? exception.Message
                    : "Bu funksiya hozircha mavjud emas. Iltimos, Excel eksportidan foydalaning.";
                break;

            case PostgresException postgresEx:
                context.Response.StatusCode = postgresEx.SqlState switch
                {
                    "23505" => (int)HttpStatusCode.Conflict,
                    "23503" => (int)HttpStatusCode.BadRequest,
                    "23502" => (int)HttpStatusCode.BadRequest,
                    "23514" => (int)HttpStatusCode.BadRequest,
                    _ => (int)HttpStatusCode.ServiceUnavailable
                };
                // Log the table/column/message too — an unmapped SqlState
                // (→ 503) is almost always schema drift (42703 undefined_column
                // / 42P01 undefined_table). Without these fields the next drift
                // incident means trawling a full stack trace; with them it's
                // one line ("column p.CategoryId does not exist", table=Products).
                _logger.LogError(
                    postgresEx,
                    "PostgreSQL error: SqlState={SqlState} Message={Message} Table={Table} Column={Column}",
                    postgresEx.SqlState,
                    postgresEx.MessageText,
                    postgresEx.TableName,
                    postgresEx.ColumnName);
                response.Message = postgresEx.SqlState switch
                {
                    "23505" => "Bu ma'lumot allaqachon mavjud.",
                    "23503" => "Bog'liq ma'lumot topilmadi.",
                    "23502" => "Majburiy maydon to'ldirilmagan.",
                    "23514" => "Ma'lumot tekshiruv shartiga mos kelmaydi.",
                    _ => "Ma'lumotlar bazasi bilan bog'lanishda xatolik yuz berdi. Iltimos, keyinroq urinib ko'ring."
                };
                break;

            case TimeoutException:
                context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                _logger.LogError(exception, "Request timeout");
                response.Message = "So'rov vaqti tugadi. Iltimos, keyinroq urinib ko'ring.";
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Message = "Serverda kutilmagan xatolik yuz berdi.";
                if (isDev)
                {
                    response.DevDetails = $"{exception.GetType().Name}: {exception.Message}";
                    response.StackTrace = exception.StackTrace;
                    response.InnerExceptionMessage = exception.InnerException?.Message;
                }
                break;
        }

        response.StatusCode = context.Response.StatusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }

    private class ErrorResponse
    {
        public int StatusCode { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TraceId { get; set; }
        public string? DevDetails { get; set; }
        public string? StackTrace { get; set; }
        public string? InnerExceptionMessage { get; set; }

        // Filled by domain-specific exceptions (e.g. MARKET_BLOCKED) so the
        // client can branch on error type without parsing the message string.
        public string? Code { get; set; }
        public string? Reason { get; set; }
        public DateTime? BlockedAt { get; set; }
        // Set by SUBSCRIPTION_EXPIRED so the client can show the lapse date.
        public DateTime? ExpiresAt { get; set; }
    }
}
