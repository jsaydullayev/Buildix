using Buildix.Application.DTOs;
using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

/// <summary>In-app bildirishnomalar — yaratish, feed, o'qilgan holati.</summary>
public interface INotificationService
{
    /// <summary>
    /// Bildirishnoma yozadi. <paramref name="dedupKey"/> berilsa va shu kalit bilan
    /// yaqinda (oxirgi ~20 soatda) yozuv bo'lsa — takrorlanmaydi. SAQLANMAYDI —
    /// chaqiruvchi SaveChanges'ida (agar tranzaksiya ichida bo'lsa) yoki
    /// mustaqil yoziladi; <paramref name="autoSave"/> bilan boshqariladi.
    /// </summary>
    Task RecordAsync(int marketId, NotificationCategory category, NotificationSeverity severity,
        string title, string text, string? actionTarget = null, string? dedupKey = null,
        bool autoSave = true, CancellationToken cancellationToken = default);

    /// <summary>Feed'ni qaytaradi. Avval holat-alertlari (kam qoldiq/qarz) yarashtiriladi.</summary>
    Task<NotificationFeedDto> GetFeedAsync(string? category, int limit = 50, CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);
    Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}
