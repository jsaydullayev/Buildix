using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
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
        // IgnoreQueryFilters: notifications also fire from background jobs where
        // there is no tenant context; the MarketId predicate does the scoping.
        var chatId = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.MarketId == marketId && u.Role == Role.Owner && u.IsActive && u.TelegramChatId != null)
            .Select(u => u.TelegramChatId)
            .FirstOrDefaultAsync(cancellationToken);
        if (chatId is null or 0) return; // owner hasn't saved their Telegram ID

        await SendToChatAsync(chatId.Value, message, cancellationToken);
    }

    public async Task SendToChatAsync(long chatId, string message, CancellationToken cancellationToken = default)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token)) return; // bot not configured — silent no-op

        try
        {
            var client = _httpFactory.CreateClient("telegram");
            var resp = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId, text = message, parse_mode = "HTML" },
                cancellationToken);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram sendMessage failed: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram send failed for chat {ChatId}", chatId);
        }
    }

    public async Task SendDocumentAsync(long chatId, byte[] content, string fileName, string? caption = null,
        CancellationToken cancellationToken = default)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(chatId.ToString()), "chat_id" },
            };
            if (!string.IsNullOrWhiteSpace(caption))
            {
                form.Add(new StringContent(caption), "caption");
                form.Add(new StringContent("HTML"), "parse_mode");
            }

            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "document", fileName);

            var client = _httpFactory.CreateClient("telegram");
            var resp = await client.PostAsync(
                $"https://api.telegram.org/bot{token}/sendDocument", form, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram sendDocument failed: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram document send failed for chat {ChatId}", chatId);
        }
    }
}
