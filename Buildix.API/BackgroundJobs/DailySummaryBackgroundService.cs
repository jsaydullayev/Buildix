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

    /// <summary>
    /// Hand the day's claim back so a later tick retries. Runs with
    /// <see cref="CancellationToken.None"/> ON PURPOSE: the usual reason to
    /// release is a send that failed because the app is shutting down, and the
    /// same cancelled token would abort the release too — marking the day sent
    /// with nothing delivered.
    /// </summary>
    private static Task ReleaseAsync(AppDbContext db, int marketId, DateTime claimStamp) =>
        db.MarketSettings
            .Where(s => s.MarketId == marketId && s.LastDaySummarySentOn == claimStamp)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastDaySummarySentOn, (DateTime?)null),
                CancellationToken.None);

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
        // Suspended shops are excluded here too. The reactive bot already refuses
        // them, but without this the nightly push would keep delivering the full
        // report (profit, till, debts) to a market the panel answers 423/402 for.
        var nowUtc = clock.UtcNow;
        var due = await db.MarketSettings
            .Where(s => s.NotifyDaySummary
                && (s.LastDaySummarySentOn == null || s.LastDaySummarySentOn < todayStampUtc))
            .Join(db.Markets.IgnoreQueryFilters()
                    .Where(m => m.IsActive && !m.IsBlocked && (m.ExpiresAt == null || m.ExpiresAt > nowUtc)),
                s => s.MarketId, m => m.Id, (s, m) => s)
            .Join(db.Users.IgnoreQueryFilters()
                    .Where(u => u.Role == Buildix.Domain.Enums.Role.Owner && u.IsActive && !u.IsDeleted
                        && u.TelegramChatId != null),
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

                // CLAIM FIRST: stamp with the "not sent today" predicate in the
                // same statement. A second instance updates 0 rows and skips, so
                // the owner never gets the summary twice. The claim is released
                // below if the send fails.
                var claimed = await db.MarketSettings
                    .Where(s => s.MarketId == market.MarketId
                        && (s.LastDaySummarySentOn == null || s.LastDaySummarySentOn < todayStampUtc))
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastDaySummarySentOn, todayStampUtc), ct);
                if (claimed == 0) continue; // another worker already sent today's

                bool sent;
                try
                {
                    // The recipient is the market owner, so the full picture.
                    var text = await summary.BuildAsync(market.MarketId, today, DailySummaryOptions.Owner, ct);
                    // The summary is its OWN message, never the file caption:
                    // captions are capped at 1024 characters and this text can
                    // exceed that, which would silently cut the report short.
                    sent = text is not null && await notifier.SendToChatAsync(market.ChatId, text, ct);
                }
                catch
                {
                    // A throw after the claim would otherwise mark the day done
                    // with nothing sent, and no later tick would retry it.
                    await ReleaseAsync(db, market.MarketId, todayStampUtc);
                    throw;
                }

                if (!sent)
                {
                    // Undelivered — release the claim so the next tick retries
                    // instead of the day being marked done with nothing sent.
                    await ReleaseAsync(db, market.MarketId, todayStampUtc);
                    _logger.LogWarning("Daily summary undelivered for market {MarketId} — claim released", market.MarketId);
                    continue;
                }

                // Then the day's receipts as a workbook. The owner sees profit,
                // so cost/profit columns are included. A failed workbook must not
                // hold back the summary that already went out.
                try
                {
                    var file = await msp.GetRequiredService<ISalesExcelExportService>()
                        .ExportSalesAsync("ru", canViewCost: true, canViewProfit: true, dayStart, dayEnd,
                            sellerId: null, ct);
                    await notifier.SendDocumentAsync(market.ChatId, file.Content,
                        $"savdo_{today:yyyy-MM-dd}.xlsx", $"<b>Kunlik savdo · {today:dd.MM.yyyy}</b>", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Daily sales workbook failed for market {MarketId}", market.MarketId);
                }

                _logger.LogInformation("Daily summary sent for market {MarketId}", market.MarketId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Daily summary failed for market {MarketId}", market.MarketId);
            }
            finally
            {
                // Drop the synthetic context before the next market. Left set, it
                // would outlive its disposed scope and silently make the next
                // iteration (or the next tick) run inside this market's tenant.
                using var cleanup = _scopeFactory.CreateScope();
                cleanup.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            }
        }
    }
}
