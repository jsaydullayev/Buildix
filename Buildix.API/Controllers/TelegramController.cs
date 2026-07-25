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
/// future notifications can reach them; afterwards that chat can pull the
/// market's day summary on demand (/kunlik).
///
/// Anonymous by design (Telegram calls it) but not unguarded: the secret token
/// proves the caller is Telegram, and every data command answers only chats
/// already linked to a market — an unlinked chat is told nothing about any shop.
/// </summary>
[ApiController]
[Route("api/telegram")]
public class TelegramController : ControllerBase
{
    private readonly ITelegramNotifier _notifier;
    private readonly ITelegramDailySummaryService _summary;
    private readonly ITashkentClock _clock;
    private readonly ILogger<TelegramController> _logger;
    private readonly IConfiguration _config;

    public TelegramController(
        ITelegramNotifier notifier,
        ITelegramDailySummaryService summary,
        ITashkentClock clock,
        ILogger<TelegramController> logger,
        IConfiguration config)
    {
        _notifier = notifier;
        _summary = summary;
        _clock = clock;
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

            var username = message.TryGetProperty("from", out var from) && from.TryGetProperty("username", out var u)
                ? u.GetString()
                : null;

            // "/kunlik@BuildixBot arg" → "kunlik". Group chats append the bot handle.
            var command = text.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]
                .TrimStart('/').Split('@')[0].ToLowerInvariant();

            await HandleCommandAsync(command, chatId, username, ct);
        }
        catch (Exception ex)
        {
            // Never fail the webhook — Telegram would retry endlessly.
            _logger.LogWarning(ex, "Telegram webhook processing error");
        }
        return Ok();
    }

    private async Task HandleCommandAsync(string command, long chatId, string? username, CancellationToken ct)
    {
        switch (command)
        {
            case "start":
                await HandleStartAsync(chatId, username, ct);
                return;

            case "kunlik":
            case "today":
            case "otchet":
            case "hisobot":
                await HandleSummaryAsync(chatId, offsetDays: 0, ct);
                return;

            case "kecha":
            case "yesterday":
                await HandleSummaryAsync(chatId, offsetDays: -1, ct);
                return;

            case "help":
            case "yordam":
                await _notifier.SendToChatAsync(chatId, HelpText, ct);
                return;

            default:
                // Unknown command from a linked chat: show what the bot can do.
                // From a stranger: stay silent — no hint that any shop exists.
                if (await _notifier.ResolveMarketByChatAsync(chatId, ct) is not null)
                    await _notifier.SendToChatAsync(chatId, HelpText, ct);
                return;
        }
    }

    private async Task HandleStartAsync(long chatId, string? username, CancellationToken ct)
    {
        // Already linked (owner pressed /start again) — just greet.
        if (await _notifier.ResolveMarketByChatAsync(chatId, ct) is not null)
        {
            await _notifier.SendToChatAsync(chatId, $"<b>Buildix</b>\nВы уже подключены.\n\n{HelpText}", ct);
            return;
        }

        var linked = !string.IsNullOrWhiteSpace(username)
                     && await _notifier.TryLinkChatAsync(username!, chatId, ct);
        if (linked)
        {
            _logger.LogInformation("Telegram chat linked for @{Username}", username);
            await _notifier.SendToChatAsync(chatId,
                $"<b>Buildix</b>\n✅ Магазин подключён. Теперь вы будете получать сводку и уведомления.\n\n{HelpText}", ct);
            return;
        }

        // No matching handle — tell them how to fix it without naming any market.
        await _notifier.SendToChatAsync(chatId,
            "<b>Buildix</b>\nВаш Telegram не привязан к магазину.\n\n" +
            "Откройте <b>Настройки → Уведомления</b> в панели Buildix, укажите свой Telegram " +
            (string.IsNullOrWhiteSpace(username) ? "@username" : $"(<code>@{username}</code>)") +
            " и отправьте сюда /start ещё раз.", ct);
    }

    private async Task HandleSummaryAsync(long chatId, int offsetDays, CancellationToken ct)
    {
        var marketId = await _notifier.ResolveMarketByChatAsync(chatId, ct);
        if (marketId is null)
        {
            await _notifier.SendToChatAsync(chatId,
                "Этот чат не привязан к магазину. Отправьте /start.", ct);
            return;
        }

        var day = _clock.TodayLocal.AddDays(offsetDays);
        var text = await _summary.BuildAsync(marketId.Value, day, ct);
        await _notifier.SendToChatAsync(chatId, text ?? "Данные недоступны.", ct);
    }

    private const string HelpText =
        "<b>Команды</b>\n" +
        "/kunlik — сводка за сегодня\n" +
        "/kecha — сводка за вчера\n" +
        "/help — это сообщение\n\n" +
        "Сводка за день приходит автоматически вечером.";
}
