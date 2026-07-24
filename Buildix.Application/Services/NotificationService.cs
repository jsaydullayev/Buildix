using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <inheritdoc cref="INotificationService"/>
public class NotificationService : INotificationService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly ITashkentClock _clock;

    // Holat-alertlari (kam qoldiq/qarz) shu oynada bir marta yoziladi — takror emas.
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(20);

    public NotificationService(IAppDbContext db, ICurrentMarketService currentMarketService, ITashkentClock clock)
    {
        _db = db;
        _currentMarketService = currentMarketService;
        _clock = clock;
    }

    public async Task RecordAsync(int marketId, NotificationCategory category, NotificationSeverity severity,
        string title, string text, string? actionTarget = null, string? dedupKey = null,
        bool autoSave = true, CancellationToken cancellationToken = default)
    {
        // Dedup: shu kalit bilan yaqinda yozuv bo'lsa — o'tkazib yuboramiz.
        if (dedupKey is not null)
        {
            var since = DateTime.UtcNow - DedupWindow;
            var exists = await _db.Notifications
                .AnyAsync(n => n.MarketId == marketId && n.DedupKey == dedupKey && n.CreatedAt >= since, cancellationToken);
            if (exists) return;
        }

        _db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Category = category,
            Severity = severity,
            Title = title,
            Text = text,
            ActionTarget = actionTarget,
            DedupKey = dedupKey,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        });

        if (autoSave)
            await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<NotificationFeedDto> GetFeedAsync(string? category, int limit = 50, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        limit = Math.Clamp(limit, 1, 200);

        // Feed so'ralganda holat-alertlarni yarashtiramiz (kam qoldiq / qarz muddati).
        await ReconcileAlertsAsync(marketId, cancellationToken);

        var query = _db.Notifications.AsNoTracking().Where(n => n.MarketId == marketId);
        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<NotificationCategory>(category, true, out var cat))
            query = query.Where(n => n.Category == cat);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationDto(
                n.Id, n.Category.ToString(), n.Severity.ToString(),
                n.Title, n.Text, n.ActionTarget, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);

        var unread = await _db.Notifications.CountAsync(n => n.MarketId == marketId && !n.IsRead, cancellationToken);
        return new NotificationFeedDto(unread, items);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        // Badge so'ralganda ham yarashtiramiz — yangi kam qoldiq darhol ko'rinsin.
        await ReconcileAlertsAsync(marketId, cancellationToken);
        return await _db.Notifications.CountAsync(n => n.MarketId == marketId && !n.IsRead, cancellationToken);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.MarketId == marketId, cancellationToken);
        if (n is not null && !n.IsRead)
        {
            n.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var unread = await _db.Notifications
            .Where(n => n.MarketId == marketId && !n.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var n in unread) n.IsRead = true;
        if (unread.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Holat-alertlarni yarashtiradi: kam/tugagan qoldiq + muddati o'tgan/bugungi
    /// qarzlar. Har biri dedup-kalit bilan yoziladi (takror emas). Bir SaveChanges.
    /// </summary>
    private async Task ReconcileAlertsAsync(int marketId, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - DedupWindow;

        // Shu oynadagi mavjud dedup-kalitlar — takrorni oldini olish uchun bir marta yuklaymiz.
        var recentKeys = (await _db.Notifications
            .Where(n => n.MarketId == marketId && n.DedupKey != null && n.CreatedAt >= since)
            .Select(n => n.DedupKey!)
            .ToListAsync(cancellationToken)).ToHashSet();

        var added = false;
        void Add(NotificationCategory cat, NotificationSeverity sev, string title, string text, string action, string key)
        {
            if (recentKeys.Contains(key)) return;
            recentKeys.Add(key);
            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(), MarketId = marketId, Category = cat, Severity = sev,
                Title = title, Text = text, ActionTarget = action, DedupKey = key,
                IsRead = false, CreatedAt = DateTime.UtcNow,
            });
            added = true;
        }

        // Kam/tugagan qoldiq
        var lowStock = await _db.Products.AsNoTracking()
            .Where(p => p.MarketId == marketId && !p.IsHidden && p.Quantity <= p.MinThreshold)
            .Select(p => new { p.Id, p.Name, p.Quantity })
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var p in lowStock)
        {
            if (p.Quantity <= 0)
                Add(NotificationCategory.Warehouse, NotificationSeverity.Danger,
                    "Товар закончился", $"«{p.Name}» — нет в наличии", "warehouse", $"outofstock:{p.Id}");
            else
                Add(NotificationCategory.Warehouse, NotificationSeverity.Warning,
                    "Мало на складе", $"«{p.Name}» — заканчивается", "warehouse", $"lowstock:{p.Id}");
        }

        // Qarz muddati — bugun / o'tgan (Toshkent kuni)
        var (todayStart, todayEnd) = _clock.LocalDayToUtcRange(_clock.TodayLocal);
        var debts = await _db.Debts.AsNoTracking()
            .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.DueDate != null && d.DueDate < todayEnd)
            .Select(d => new { d.Id, d.DueDate, d.RemainingDebt, CustomerName = d.Customer.FullName })
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var d in debts)
        {
            if (d.DueDate < todayStart)
                Add(NotificationCategory.Debt, NotificationSeverity.Danger,
                    "Долг просрочен", $"{d.CustomerName ?? "Клиент"} · {d.RemainingDebt:N0} сум", "debts", $"debt-overdue:{d.Id}");
            else
                Add(NotificationCategory.Debt, NotificationSeverity.Warning,
                    "Срок оплаты долга — сегодня", $"{d.CustomerName ?? "Клиент"} · {d.RemainingDebt:N0} сум", "debts", $"debt-today:{d.Id}");
        }

        if (added) await _db.SaveChangesAsync(cancellationToken);
    }
}
