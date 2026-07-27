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

    // Bot API hard limits. Exceeding either makes Telegram reject the WHOLE
    // request with 400, so the user would silently get nothing — worse than a
    // trimmed message. Clamping here covers every caller at one choke point.
    private const int MaxMessageLength = 4096;
    private const int MaxCaptionLength = 1024;

    /// <summary>
    /// Clamp to the API limit, cutting only where it is safe to cut.
    ///
    /// A naive substring is worse than the overflow it fixes: slicing through a
    /// <c>&lt;b&gt;</c> tag, an <c>&amp;amp;</c> entity or an emoji's surrogate
    /// pair makes Telegram answer «can't parse entities» and drop the ENTIRE
    /// message. Cutting at the last newline keeps every tag balanced, because
    /// each line this codebase emits opens and closes its own markup.
    /// </summary>
    private static string Clamp(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;

        var cut = text[..(limit - 1)];
        var lastBreak = cut.LastIndexOf('\n');
        if (lastBreak > 0)
            return cut[..lastBreak] + "\n…";

        // One enormous line with no break: fall back to a raw cut, but never
        // between the halves of a surrogate pair, and drop any half-open tag or
        // entity that the cut may have left behind.
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        var openTag = cut.LastIndexOf('<');
        if (openTag >= 0 && cut.IndexOf('>', openTag) < 0) cut = cut[..openTag];
        var openEntity = cut.LastIndexOf('&');
        if (openEntity >= 0 && cut.IndexOf(';', openEntity) < 0) cut = cut[..openEntity];

        // A COMPLETE opening tag whose closer fell past the cut is just as fatal
        // as a half-open one — Telegram answers "can't parse entities" and drops
        // the message. Close whatever is still open, innermost first.
        return cut + CloseDanglingTags(cut) + "…";
    }

    /// <summary>Closing tags for any formatting element left open in <paramref name="html"/>.</summary>
    private static string CloseDanglingTags(string html)
    {
        var open = new Stack<string>();
        foreach (var tag in new[] { "b", "i", "u", "s", "code", "pre" })
        {
            var opened = CountOccurrences(html, $"<{tag}>");
            var closed = CountOccurrences(html, $"</{tag}>");
            for (var i = 0; i < opened - closed; i++) open.Push(tag);
        }
        return open.Count == 0 ? string.Empty : string.Concat(open.Select(t => $"</{t}>"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    public async Task SendToOwnerAsync(int marketId, string message, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: notifications also fire from background jobs where
        // there is no tenant context; the MarketId predicate does the scoping.
        var chatId = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.MarketId == marketId && u.Role == Role.Owner && u.IsActive && !u.IsDeleted
                && u.TelegramChatId != null)
            // Ordered so a market with two Owner rows always notifies the same
            // one instead of whichever the plan happened to return.
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.TelegramChatId)
            .FirstOrDefaultAsync(cancellationToken);
        if (chatId is null or 0) return; // owner hasn't saved their Telegram ID

        await SendToChatAsync(chatId.Value, message, cancellationToken);
    }

    public async Task<bool> SendToChatAsync(long chatId, string message, CancellationToken cancellationToken = default)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token)) return false; // bot not configured — silent no-op

        try
        {
            var client = _httpFactory.CreateClient("telegram");
            var resp = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId, text = Clamp(message, MaxMessageLength), parse_mode = "HTML" },
                cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram sendMessage failed: {Status}", resp.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram send failed for chat {ChatId}", chatId);
            return false;
        }
    }

    public async Task SendWithKeyboardAsync(long chatId, string message,
        IReadOnlyList<IReadOnlyList<string>> keyboard, CancellationToken cancellationToken = default)
    {
        var token = Token;
        if (string.IsNullOrWhiteSpace(token)) return;

        // An empty keyboard removes the buttons instead of sending an empty one —
        // Telegram rejects a keyboard with no rows.
        object replyMarkup = keyboard.Count == 0
            ? new { remove_keyboard = true }
            : new
            {
                keyboard = keyboard.Select(row => row.Select(text => new { text }).ToArray()).ToArray(),
                resize_keyboard = true,
                is_persistent = true,
            };

        try
        {
            var client = _httpFactory.CreateClient("telegram");
            var resp = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{token}/sendMessage",
                new
                {
                    chat_id = chatId,
                    text = Clamp(message, MaxMessageLength),
                    parse_mode = "HTML",
                    reply_markup = replyMarkup,
                },
                cancellationToken);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Telegram sendMessage (keyboard) failed: {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram keyboard send failed for chat {ChatId}", chatId);
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
                // A caption over 1024 chars makes Telegram reject the whole
                // upload, so the report would be lost rather than shortened.
                form.Add(new StringContent(Clamp(caption, MaxCaptionLength)), "caption");
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
