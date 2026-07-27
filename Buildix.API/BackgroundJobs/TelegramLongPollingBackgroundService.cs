using System.Text.Json;
using Buildix.API.Services;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Telegram <c>getUpdates</c> long-polling — webhook o'rniga ishlaydigan rejim.
///
/// <para><b>Nega kerak.</b> Webhook Telegram'dan KIRUVCHI so'rov talab qiladi,
/// ya'ni serverning ochiq HTTPS manzili bo'lishi shart. Lokal ishlab chiqishda
/// (va ochiq IP'siz serverlarda) bunday manzil yo'q — bot xabarlarni umuman
/// olmaydi. Long-polling esa teskari yo'nalishda ishlaydi: server o'zi
/// Telegram'dan so'rab turadi, hech qanday ochiq port kerak emas.</para>
///
/// <para><b>Yoqish:</b> <c>Telegram:UseLongPolling = true</c> (env
/// <c>Telegram__UseLongPolling</c>). O'chiq bo'lsa bu servis umuman ishga
/// tushmaydi va webhook rejimi o'zgarishsiz qoladi. Ikkalasi bir vaqtda ishlay
/// olmaydi (Telegram cheklovi) — yoqilганda avval <c>deleteWebhook</c>
/// chaqiriladi, bu ataylab: operator long-polling'ni tanlagan.</para>
///
/// <para>Har bir update xuddi webhook'dagidek qayta ishlanadi:
/// <see cref="ITelegramBotCommandHandler"/> ga chat id + matn beriladi. Handler
/// va undan keyingi eksport servislari <c>HttpContext.Items["MarketId"]</c>
/// orqali market ko'lamini oladi — bu yerda so'rov bo'lmagani uchun sun'iy
/// <see cref="DefaultHttpContext"/> yaratib, <see cref="IHttpContextAccessor"/>
/// ga o'rnatamiz: webhook semantikasi bilan aynan bir xil.</para>
/// </summary>
public class TelegramLongPollingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramLongPollingBackgroundService> _logger;

    public TelegramLongPollingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<TelegramLongPollingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = _config["Telegram:BotToken"];
        var enabled = _config.GetValue<bool>("Telegram:UseLongPolling");
        if (!enabled || string.IsNullOrWhiteSpace(token))
        {
            if (enabled)
                _logger.LogWarning("Telegram long-polling enabled but Telegram:BotToken is empty — polling not started.");
            return;
        }

        // Migratsiya/seed tugashini kutamiz.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var api = $"https://api.telegram.org/bot{token}";
        var http = _httpFactory.CreateClient("telegram-poll");
        // getUpdates timeout=25s ushlab turadi; HttpClient undan uzunroq kutsin.
        http.Timeout = TimeSpan.FromSeconds(40);

        // Webhook bilan long-polling birga ishlamaydi — ochiq niyat bilan o'chiramiz.
        try
        {
            await http.PostAsync($"{api}/deleteWebhook", null, stoppingToken);
            _logger.LogInformation("Telegram long-polling active (webhook removed).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "deleteWebhook failed — long-polling may conflict with an active webhook.");
        }

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var url = $"{api}/getUpdates?timeout=25&allowed_updates=[\"message\"]"
                          + (offset > 0 ? $"&offset={offset}" : "");
                using var resp = await http.GetAsync(url, stoppingToken);
                var body = await resp.Content.ReadAsStringAsync(stoppingToken);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    // 409 = boshqa nusxa polling qilyapti yoki webhook qaytdi.
                    _logger.LogWarning("getUpdates not ok: {Body}", body.Length > 300 ? body[..300] : body);
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                foreach (var update in doc.RootElement.GetProperty("result").EnumerateArray())
                {
                    offset = Math.Max(offset, update.GetProperty("update_id").GetInt64() + 1);
                    await DispatchAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Tarmoq uzilishi tsiklni o'ldirmasin.
                _logger.LogWarning(ex, "Telegram polling pass failed");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Bitta update — webhook bilan bir xil yo'l: matnli xabar bo'lsa
    /// handler'ga. Xato bitta xabarni tashlab yuboradi, navbatni emas.</summary>
    private async Task DispatchAsync(JsonElement update, CancellationToken ct)
    {
        try
        {
            if (!update.TryGetProperty("message", out var message)) return;
            var text = message.TryGetProperty("text", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!message.TryGetProperty("chat", out var chat) ||
                !chat.TryGetProperty("id", out var chatIdEl)) return;
            var chatId = chatIdEl.GetInt64();

            using var scope = _scopeFactory.CreateScope();

            // Handler zanjiri (eksport servislari) MarketId'ni HttpContext'dan
            // o'qiydi — webhook'da buni haqiqiy so'rov beradi, bu yerda esa
            // xuddi shunday kontekstni o'zimiz yaratamiz.
            var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = httpContext;
            try
            {
                var handler = scope.ServiceProvider.GetRequiredService<ITelegramBotCommandHandler>();
                await handler.HandleAsync(chatId, text, httpContext, ct);
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram polled update processing error");
        }
    }
}
