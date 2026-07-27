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
    /// <summary>How many product lines the alert message itself carries.</summary>
    private const int MaxListedInMessage = 20;

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
            // Ordered so the cap takes a defined slice: without it Postgres may
            // return any 1000 rows and a market could be starved for many passes.
            // Emptiest first — those are the ones worth telling first anyway.
            .OrderBy(p => p.Quantity).ThenBy(p => p.Id)
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
                // Suspended shops get nothing — the panel answers 423/402 for
                // them, so the bot must not keep pushing either.
                var live = await db.Markets.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(m => m.Id == marketId && m.IsActive && !m.IsBlocked
                        && (m.ExpiresAt == null || m.ExpiresAt > now), ct);
                if (!live) continue;

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

                // CLAIM FIRST, then send. The stamp is written with the
                // "still unclaimed" predicate in the same statement, so a second
                // instance (or an overlapping tick) updates 0 rows and stays
                // silent — read-then-stamp would have let both send.
                var ids = items.Select(i => i.Id).ToList();
                var claimed = await db.Products.IgnoreQueryFilters()
                    .Where(p => ids.Contains(p.Id) && p.LowStockAlertSentAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(p => p.LowStockAlertSentAt, now), ct);
                if (claimed == 0) continue; // another worker got there first

                if (chats.Count == 0)
                {
                    // Nobody to tell. The claim stands: without it this market
                    // would be re-evaluated every 15 minutes forever.
                    _logger.LogInformation("Low-stock: {Count} product(s), market {MarketId}, no linked recipients",
                        items.Count, marketId);
                    continue;
                }

                var delivered = 0;
                try
                {
                    var text = BuildMessage(items.Select(i => (i.Name, i.Quantity)).ToList());
                    foreach (var chat in chats)
                        if (await notifier.SendToChatAsync(chat, text, ct)) delivered++;
                }
                catch
                {
                    // Any throw between claim and delivery must not swallow the
                    // claim: fall through to the release below, then rethrow.
                    await ReleaseAsync(db, ids, now);
                    throw;
                }

                if (delivered == 0)
                {
                    // Nothing reached Telegram — RELEASE the claim so the next
                    // tick retries. Stamping through a failure would consume the
                    // single alert this depletion ever gets.
                    await ReleaseAsync(db, ids, now);
                    _logger.LogWarning("Low-stock alert undelivered for market {MarketId} — claim released", marketId);
                    continue;
                }

                if (delivered < chats.Count)
                    // Deliberate: the claim stands. Retrying for the rest would
                    // re-alert those who already got it, breaking once-per-product.
                    _logger.LogWarning("Low-stock alert reached {Ok}/{All} chat(s), market {MarketId}",
                        delivered, chats.Count, marketId);

                _logger.LogInformation("Low-stock alert: {Count} product(s), market {MarketId}, {Chats} chat(s)",
                    items.Count, marketId, delivered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Low-stock alert failed for market {MarketId}", marketId);
            }
        }
    }

    /// <summary>
    /// Hand the claim back so a later tick retries.
    ///
    /// Runs with <see cref="CancellationToken.None"/> ON PURPOSE: the usual
    /// reason to release is that the send failed because the app is shutting
    /// down, and passing that same cancelled token would abort the release too —
    /// leaving the products stamped with nothing delivered, which for a
    /// once-per-product alert means it is never sent at all.
    /// </summary>
    private static Task ReleaseAsync(AppDbContext db, List<Guid> ids, DateTime claimStamp) =>
        db.Products.IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id) && p.LowStockAlertSentAt == claimStamp)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.LowStockAlertSentAt, (DateTime?)null),
                CancellationToken.None);

    /// <summary>
    /// Lists at most <see cref="MaxListedInMessage"/> products and counts the
    /// rest. A message is capped at 4096 characters by Telegram; 50 long product
    /// names would blow past it and the alert would be REJECTED — and since the
    /// products get stamped either way, a once-per-product alert would be lost
    /// for good. The workbook behind «📦 Kam qolgan» has the full list.
    /// </summary>
    private static string BuildMessage(IReadOnlyList<(string Name, decimal Quantity)> items)
    {
        var sb = new StringBuilder("<b>⚠️ Kam qolgan mahsulotlar</b>\n");
        foreach (var (name, qty) in items.Take(MaxListedInMessage))
            sb.Append(qty <= 0 ? "🔴 " : "🟡 ")
              .Append(Escape(Shorten(name)))
              .Append(" — ")
              .Append(qty <= 0 ? "tugadi" : qty.ToString("0.##"))
              .Append('\n');
        if (items.Count > MaxListedInMessage)
            sb.Append("… va yana ").Append(items.Count - MaxListedInMessage).Append(" ta\n");
        sb.Append("\nTo'liq ro'yxat: «📦 Kam qolgan» tugmasi");
        return sb.ToString();
    }

    private static string Shorten(string name) =>
        name.Length <= 60 ? name : name[..59] + "…";

    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
