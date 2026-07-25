using System.Security.Cryptography;
using System.Text.Json;
using Buildix.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buildix.API.Controllers;

/// <summary>
/// Telegram Bot webhook. Register it with the Bot API:
/// <c>setWebhook url=https://&lt;host&gt;/api/telegram/webhook secret_token=…</c>.
///
/// One bot serves every market. A message is only ever answered when its chat id
/// matches a <c>User.TelegramChatId</c> saved on the Account screen — that lookup
/// yields the market and the user's permissions, and each command is gated by
/// them. An unknown chat learns nothing about any shop.
///
/// Anonymous by design (Telegram calls it), but the secret token proves the
/// caller is Telegram; without a configured secret the endpoint fails closed.
/// </summary>
[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramBotCommandHandler _handler;
    private readonly ILogger<TelegramController> _logger;
    private readonly IConfiguration _config;

    public TelegramController(
        ITelegramBotCommandHandler handler,
        ILogger<TelegramController> logger,
        IConfiguration config)
    {
        _handler = handler;
        _logger = logger;
        _config = config;
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook([FromBody] JsonElement update, CancellationToken ct)
    {
        // H-8: only Telegram may call this. setWebhook is registered with a
        // secret_token; Telegram echoes it in this header on every update. A
        // request without the matching secret is a forgery — drop it silently
        // (200 so a misconfigured caller isn't told the secret exists). If no
        // secret is configured the webhook is disabled entirely (fail closed).
        var expected = _config["Telegram:WebhookSecret"];
        var provided = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(provided), System.Text.Encoding.UTF8.GetBytes(expected)))
        {
            _logger.LogWarning("Telegram webhook rejected: missing/invalid secret token");
            return Ok();
        }

        try
        {
            if (!update.TryGetProperty("message", out var message)) return Ok();

            var text = message.TryGetProperty("text", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)) return Ok();

            if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl))
                return Ok();
            var chatId = chatIdEl.GetInt64();

            await _handler.HandleAsync(chatId, text, HttpContext, ct);
        }
        catch (Exception ex)
        {
            // Never fail the webhook — Telegram would retry endlessly.
            _logger.LogWarning(ex, "Telegram webhook processing error");
        }
        return Ok();
    }
}
