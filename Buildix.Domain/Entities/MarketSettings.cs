using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Per-market configuration backing the Настройки (Settings) screen. One row
/// per <see cref="Market"/> (1:1, shares the market's int key). Every business
/// toggle/limit the design exposes lives here so the rules can be enforced in
/// the sales/shift/debt flows instead of only being cosmetic UI state.
///
/// Defaults mirror the values shown in the Settings mockup so a freshly
/// provisioned market behaves sensibly before the owner ever opens the screen.
/// </summary>
public class MarketSettings
{
    /// <summary>PK == FK to Market.Id (1:1).</summary>
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // ── Магазин (store profile) ──────────────────────────────────────────
    public string? Phone { get; set; }
    public string? Address { get; set; }
    /// <summary>Free-form working hours, e.g. "08:00 — 20:00".</summary>
    public string? WorkingHours { get; set; }

    // ── Касса и смены (cash & shift rules) ───────────────────────────────
    /// <summary>Kassir smena ochmasdan sota olmaydi.</summary>
    public bool SalesOnlyWhenShiftOpen { get; set; } = true;
    /// <summary>Naqd yechish egasi tasdig'ini talab qiladi (approval oqimi).</summary>
    public bool CashWithdrawalNeedsApproval { get; set; } = true;
    /// <summary>Qarzga sotish faqat "postoyanniy" mijozlarga.</summary>
    public bool DebtOnlyForRegulars { get; set; } = true;
    /// <summary>Bitta mijozga standart qarz limiti (sum). 0 = limitsiz.</summary>
    public decimal DefaultDebtLimit { get; set; } = 15_000_000m;
    /// <summary>Kassada ruxsat etilgan maksimal расхождение (sum).</summary>
    public decimal AllowedCashDiscrepancy { get; set; } = 0m;
    /// <summary>Smena avto-yopilish vaqti (HH:mm), null = avto-yopish yo'q.</summary>
    public TimeOnly? ShiftAutoCloseTime { get; set; }

    // ── Посещаемость (davomat hisobi — §2.15 Смены) ─────────────────────────
    // Do'kon ish grafigi: reja soati = End − Start; smena shu vaqtdan keyin
    // ochilsa "kechikish". Standart 08:00–20:00 · 08:15 — mavjud xatti-harakat.
    /// <summary>Ish kuni boshlanishi (davomat rejasi).</summary>
    public TimeOnly WorkDayStart { get; set; } = new(8, 0);
    /// <summary>Ish kuni tugashi (davomat rejasi).</summary>
    public TimeOnly WorkDayEnd { get; set; } = new(20, 0);
    /// <summary>Kechikish chegarasi — shundan keyin ochilsa "опоздание".</summary>
    public TimeOnly LateThreshold { get; set; } = new(8, 15);

    // ── Чек (receipt) ────────────────────────────────────────────────────
    public string? ReceiptHeader { get; set; }
    public string? ReceiptFooter { get; set; }
    public bool AutoPrintReceipt { get; set; } = false;

    // ── Локаль (locale) ──────────────────────────────────────────────────
    public Language DefaultLanguage { get; set; } = Language.Russian;
    /// <summary>Hafta boshi: 1 = Dushanba (ISO), 7 = Yakshanba.</summary>
    public int FirstDayOfWeek { get; set; } = 1;

    // ── Склад и цены (warehouse & pricing) ───────────────────────────────
    public bool MinStockAlertEnabled { get; set; } = true;
    /// <summary>Sotuv narxi tannarxdan past bo'lsa bloklanadi.</summary>
    public bool BlockSaleBelowCost { get; set; } = true;
    /// <summary>Yangi mahsulot uchun standart ustama (%).</summary>
    public decimal DefaultMarkupPct { get; set; } = 18m;

    // ── Уведомления (Telegram notifications) ─────────────────────────────
    public bool NotifyDaySummary { get; set; } = true;
    public bool NotifyOverdueDebts { get; set; } = true;
    public bool NotifyWithdrawalRequests { get; set; } = true;
    // Telegram bog'lash MarketSettings'dan User.TelegramChatId'ga ko'chirildi:
    // bot endi har bir XODIMNI o'z ID si bo'yicha taniydi (faqat egasini emas),
    // shuning uchun bu yerda market darajasidagi @username/chat_id saqlanmaydi.
    // Bu blokda faqat market darajasidagi Notify* kalitlari qoladi.
    /// <summary>
    /// Kunlik xulosa oxirgi yuborilgan Toshkent kunining UTC boshlanishi.
    /// (Sana emas, UTC instant — ustun `timestamptz`, Npgsql Kind=Unspecified
    /// qiymatni qabul qilmaydi.) Fon vazifasi shu bilan kuniga bir marta
    /// yuborishni kafolatlaydi — qayta ishga tushirish ham takror yubormaydi.
    /// </summary>
    public DateTime? LastDaySummarySentOn { get; set; }

    // ── Безопасность (security) ──────────────────────────────────────────
    /// <summary>Harakatsizlikда avto-chiqish (daqiqa). 0 = o'chirilgan.</summary>
    public int InactivityLogoutMinutes { get; set; } = 0;
    public bool AuditEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
