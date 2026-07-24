using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// In-app bildirishnoma — Уведомления feed'idagi bir element. Domen hodisalari
/// (kam qoldiq, qarz muddati, smena yopilishi, xarid qabuli) tomonidan
/// yaratiladi va o'qilgan/o'qilmagan holati saqlanadi.
/// </summary>
public class Notification : BaseEntity
{
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    /// <summary>Kimga (null = market egasi/admin ko'radi). Hozircha market-darajali.</summary>
    public Guid? UserId { get; set; }

    public NotificationCategory Category { get; set; }
    public NotificationSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>Bosilganda o'tiladigan ichki manzil, masalan "warehouse" yoki "debts". Ixtiyoriy.</summary>
    public string? ActionTarget { get; set; }

    /// <summary>
    /// Holat-alertlari (kam qoldiq/qarz) uchun takrorlanishni oldini oluvchi
    /// kalit, masalan "lowstock:{productId}". Shu kalit bilan yaqinda yozuv bo'lsa
    /// yangi yaratilmaydi. Hodisa-alertlari (smena/xarid) uchun null (doim yangi).
    /// </summary>
    public string? DedupKey { get; set; }

    public bool IsRead { get; set; } = false;
}
