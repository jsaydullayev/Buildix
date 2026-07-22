using System.Net.Http.Json;
using Buildix.Application.Interfaces;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Buildix.Infrastructure.Services;

/// <summary>
/// Real Telegram Bot API notifier. Token comes from configuration
/// (<c>Telegram:BotToken</c> / env <c>Telegram__BotToken</c>). Best-effort —
/// swallows all transport errors so a Telegram outage never breaks a sale,
/// shift close, or debt update.
/// </summary>
public class TelegramNotifier : ITelegramNotifier
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(
        IHttpClientFactory httpFactory,
        AppDbContext db,
        IConfiguration config,
        ILogger<TelegramNotifier> logger)
    {
        _httpFactory = httpFactory;
        _db = db;
        _config = config;
        _logger = logger;
    }

    private string? Token => _config["Telegram:BotToken"];

    public async Task SendToOwnerAsync(int marketId, string message, CancellationToken cancellationToken = default)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token)) return; // bot not configured — silent no-op

        var chatId = await _db.MarketSettings
            .Where(s => s.MarketId == marketId)
            .Select(s => s.OwnerTelegramChatId)
            .FirstOrDefaultAsync(cancellationToken);
        if (chatId is null or 0) return; // owner hasn't linked their chat

        try
        {
            var client = _httpFactory.CreateClient("telegram");
            var resp = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId.Value, text = message, parse_mode = "HTML" },
                cancellationToken);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram sendMessage failed: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram notification failed for market {MarketId}", marketId);
        }
    }

    public async Task<bool> TryLinkChatAsync(string username, long chatId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var handle = username.StartsWith('@') ? username : '@' + username;

        var settings = await _db.MarketSettings
            .FirstOrDefaultAsync(s => s.OwnerTelegram != null && s.OwnerTelegram.ToLower() == handle.ToLower(), cancellationToken);
        if (settings is null) return false;

        settings.OwnerTelegramChatId = chatId;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
