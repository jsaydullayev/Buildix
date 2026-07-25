using Buildix.Application.Interfaces;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Sends every market's day summary to its owner's Telegram once a day, after
/// the configured local hour (<c>Telegram:DailySummaryHour</c>, default 21).
///
/// Design notes:
/// • Polls every 10 minutes rather than sleeping until the exact hour — a server
///   restart or clock jump can't make the day slip through unsent.
/// • <c>MarketSettings.LastDaySummarySentOn</c> is the idempotency key, so a
///   restart (or a second instance) re-sends nothing.
/// • Runs outside any HTTP request: the tenant query-filter is inert here, which
///   is exactly why every query below filters by MarketId explicitly and the
///   summary service takes marketId as a parameter.
/// </summary>
public class DailySummaryBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private const int DefaultHour = 21;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DailySummaryBackgroundService> _logger;

    public DailySummaryBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<DailySummaryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to finish migrations/seeding before the first pass.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A bad day must not kill the loop — log and try again next tick.
                _logger.LogError(ex, "Daily summary pass failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var sendHour = _config.GetValue<int?>("Telegram:DailySummaryHour") ?? DefaultHour;
        if (sendHour is < 0 or > 23) sendHour = DefaultHour;

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<ITashkentClock>();

        var nowLocal = clock.NowLocal;
        if (nowLocal.Hour < sendHour) return; // too early — nothing to do this tick

        var today = clock.TodayLocal.Date;
        // The stamp is stored as the UTC instant that starts this local day:
        // TodayLocal has Kind=Unspecified and Npgsql rejects that for a
        // `timestamp with time zone` column. Comparing UTC-start values still
        // answers "was the summary already sent for this local day?".
        var todayStampUtc = clock.LocalDayToUtcRange(today).UtcStart;
        var db = sp.GetRequiredService<AppDbContext>();

        // Markets that want the summary and haven't got today's yet. The
        // recipient is the market owner's saved Telegram id (User.TelegramChatId);
        // markets whose owner hasn't linked one are skipped.
        var due = await db.MarketSettings
            .Where(s => s.NotifyDaySummary
                && (s.LastDaySummarySentOn == null || s.LastDaySummarySentOn < todayStampUtc))
            .Join(db.Users.IgnoreQueryFilters()
                    .Where(u => u.Role == Buildix.Domain.Enums.Role.Owner && u.IsActive && u.TelegramChatId != null),
                s => s.MarketId, u => u.MarketId,
                (s, u) => new { s.MarketId, ChatId = u.TelegramChatId!.Value })
            .ToListAsync(ct);
        if (due.Count == 0) return;

        var (dayStart, dayEnd) = clock.LocalDayToUtcRange(today);

        foreach (var market in due)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                // A SEPARATE scope per market, each with its own synthetic
                // HttpContext carrying that market's id.
                //
                // This matters for correctness, not tidiness: the Excel export
                // reaches the tenant through ICurrentMarketService →
                // HttpContext.Items["MarketId"]. With no context that lookup
                // returns null, the global query filter switches off, and the
                // "daily sales" workbook would contain EVERY market's sales.
                // Filling the same slot the middleware fills scopes it correctly.
                using var marketScope = _scopeFactory.CreateScope();
                var msp = marketScope.ServiceProvider;
                var accessor = msp.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = new DefaultHttpContext { RequestServices = msp };
                accessor.HttpContext.Items["MarketId"] = market.MarketId;

                var summary = msp.GetRequiredService<ITelegramDailySummaryService>();
                var notifier = msp.GetRequiredService<ITelegramNotifier>();

                var text = await summary.BuildAsync(market.MarketId, today, ct);
                if (text is null) continue; // market vanished — leave the stamp alone

                // Text first (readable on a phone at a glance), then the
                // spreadsheet with the day's receipts. The owner sees profit, so
                // cost/profit columns are included.
                byte[]? excel = null;
                string? excelName = null;
                try
                {
                    var file = await msp.GetRequiredService<ISalesExcelExportService>()
                        .ExportSalesAsync("ru", canViewCost: true, canViewProfit: true, dayStart, dayEnd, ct);
                    excel = file.Content;
                    excelName = $"savdo_{today:yyyy-MM-dd}.xlsx";
                }
                catch (Exception ex)
                {
                    // The summary is the point; a failed workbook must not hold it back.
                    _logger.LogWarning(ex, "Daily sales workbook failed for market {MarketId}", market.MarketId);
                }

                if (excel is not null)
                    await notifier.SendDocumentAsync(market.ChatId, excel, excelName!, text, ct);
                else
                    await notifier.SendToChatAsync(market.ChatId, text, ct);

                // Stamp only after a send was attempted, so a crash mid-loop
                // retries this market rather than skipping its day.
                await db.MarketSettings
                    .Where(s => s.MarketId == market.MarketId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastDaySummarySentOn, todayStampUtc), ct);

                _logger.LogInformation("Daily summary sent for market {MarketId}", market.MarketId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Daily summary failed for market {MarketId}", market.MarketId);
            }
        }
    }
}
