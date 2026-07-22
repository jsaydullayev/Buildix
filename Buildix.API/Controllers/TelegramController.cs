using System.Security.Cryptography;
using System.Text.Json;
using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Buildix.API.Controllers;

/// <summary>
/// Telegram Bot webhook. Set it with the Bot API setWebhook to
/// https://&lt;host&gt;/api/telegram/webhook. When an owner sends /start we match
/// their @username to MarketSettings.OwnerTelegram and store the chat id so
/// future notifications can reach them. Anonymous by design (Telegram calls it);
/// it performs no privileged action beyond linking a chat to a matching handle.
/// </summary>
[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramNotifier _notifier;
    private readonly ILogger<TelegramController> _logger;
    private readonly IConfiguration _config;

    public TelegramController(ITelegramNotifier notifier, ILogger<TelegramController> logger, IConfiguration config)
    {
        _notifier = notifier;
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
            if (text is null || !text.TrimStart().StartsWith("/start", StringComparison.OrdinalIgnoreCase))
                return Ok();

            if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl))
                return Ok();
            var chatId = chatIdEl.GetInt64();

            var username = message.TryGetProperty("from", out var from) && from.TryGetProperty("username", out var u)
                ? u.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(username)) return Ok();

            var linked = await _notifier.TryLinkChatAsync(username, chatId, ct);
            if (linked)
                _logger.LogInformation("Telegram chat linked for @{Username}", username);
        }
        catch (Exception ex)
        {
            // Never fail the webhook — Telegram would retry endlessly.
            _logger.LogWarning(ex, "Telegram webhook processing error");
        }
        return Ok();
    }
}
