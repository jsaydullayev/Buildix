using System.Text;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Telegram "kam qolgan mahsulot" alerts — <b>once per product</b>.
///
/// <para>A product is announced when its quantity first falls to or below its
/// minimum; <c>Product.LowStockAlertSentAt</c> is then stamped so it is never
/// repeated. The stamp is cleared once the stock recovers above the minimum, so
/// the next depletion alerts again. That pair is what makes "only once" true
/// across restarts without spamming a shop whose stock hovers at the threshold.</para>
///
/// <para>Recipients: every active user of the market who holds
/// <c>products.access</c>, kept their stock notifications on, and saved a
/// Telegram id. Revoking the permission silently stops the alerts — the same
/// "off → invisible" rule the panel follows.</para>
///
/// <para>Runs outside any HTTP request, so the tenant query-filter is inert and
/// every query below scopes by MarketId explicitly.</para>
/// </summary>
public class LowStockAlertBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
    private const int MaxProductsPerMarket = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LowStockAlertBackgroundService> _logger;

    public LowStockAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LowStockAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
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
                _logger.LogError(ex, "Low-stock alert pass failed");
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
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var clock = sp.GetRequiredService<ITashkentClock>();
        var notifier = sp.GetRequiredService<ITelegramNotifier>();
        var now = clock.UtcNow;

        // 1) Recovered products — clear the stamp so a future drop alerts again.
        await db.Products.IgnoreQueryFilters()
            .Where(p => p.LowStockAlertSentAt != null && p.Quantity > p.MinThreshold)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.LowStockAlertSentAt, (DateTime?)null), ct);

        // 2) Newly depleted products, grouped by market.
        var fresh = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsHidden
                && p.Quantity <= p.MinThreshold
                && p.LowStockAlertSentAt == null
                && p.MarketId != 0)
            .Select(p => new { p.Id, p.MarketId, p.Name, p.Quantity, p.MinThreshold })
            .Take(MaxProductsPerMarket * 20)
            .ToListAsync(ct);
        if (fresh.Count == 0) return;

        foreach (var group in fresh.GroupBy(p => p.MarketId))
        {
            if (ct.IsCancellationRequested) return;
            var marketId = group.Key;
            var items = group.Take(MaxProductsPerMarket).ToList();

            try
            {
                var recipients = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.MarketId == marketId && u.IsActive && !u.IsDeleted
                        && u.TelegramChatId != null && u.NotifyStock)
                    .Select(u => new { u.TelegramChatId, u.Role, u.Permissions, u.IsPermissionsCustomized })
                    .ToListAsync(ct);

                // Permission check runs in memory: GetEffectivePermissions() folds
                // role defaults, the custom set and the seller-forbidden list, and
                // that logic can't be translated to SQL.
                var chats = recipients
                    .Where(r => new Domain.Entities.User
                    {
                        Role = r.Role,
                        Permissions = r.Permissions,
                        IsPermissionsCustomized = r.IsPermissionsCustomized,
                    }.HasPermission(PermissionKeys.ProductsAccess))
                    .Select(r => r.TelegramChatId!.Value)
                    .Distinct()
                    .ToList();

                if (chats.Count > 0)
                {
                    var text = BuildMessage(items.Select(i => (i.Name, i.Quantity)).ToList());
                    foreach (var chat in chats)
                        await notifier.SendToChatAsync(chat, text, ct);
                }

                // Stamp regardless of whether anyone was reachable: the alert for
                // this depletion is done. Without this, a market with no linked
                // users would re-evaluate the same products every 15 minutes.
                var ids = items.Select(i => i.Id).ToList();
                await db.Products.IgnoreQueryFilters()
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.LowStockAlertSentAt, now), ct);

                _logger.LogInformation("Low-stock alert: {Count} product(s), market {MarketId}, {Chats} chat(s)",
                    items.Count, marketId, chats.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Low-stock alert failed for market {MarketId}", marketId);
            }
        }
    }

    private static string BuildMessage(IReadOnlyList<(string Name, decimal Quantity)> items)
    {
        var sb = new StringBuilder("<b>⚠️ Kam qolgan mahsulotlar</b>\n");
        foreach (var (name, qty) in items)
            sb.Append(qty <= 0 ? "🔴 " : "🟡 ")
              .Append(Escape(name))
              .Append(" — ")
              .Append(qty <= 0 ? "tugadi" : qty.ToString("0.##"))
              .Append('\n');
        sb.Append("\nTo'liq ro'yxat: /qoldiq");
        return sb.ToString();
    }

    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
