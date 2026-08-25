using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

/// <summary>
/// Platforma sozlamalari — BITTA qator (<c>Id = 1</c>).
///
/// <para><c>MarketSettings</c> har do'kon uchun, bu esa butun platforma
/// uchun: blok qoidalari, SMS toggle'lari va kirish sahifasidagi
/// qo'llab-quvvatlash kontaktlari. Tarif narxlari bu yerda EMAS —
/// ular <see cref="PlatformPlan"/> jadvalida (uch qator).</para>
///
/// <para>Har so'rovda o'qilmaydi: <c>IPlatformSettingsProvider</c> uni
/// keshda saqlaydi va faqat yozuvdan keyin yangilaydi.</para>
/// </summary>
public class PlatformSettings : IUpdateTracked
{
    /// <summary>Har doim 1 — jadval bitta qatorli.</summary>
    public int Id { get; set; } = 1;

    // ── Blok qoidalari ──────────────────────────────────────────────────────

    /// <summary>Muddat tugagach xizmat ochiq qoladigan kunlar soni.</summary>
    public int GraceDays { get; set; } = 5;

    /// <summary>
    /// Otsrochkaning 1-kunidan boshlab egaga va adminga sariq plashka.
    /// O'chirilsa foydalanuvchi hech narsa sezmaydi (holat javob header'ida
    /// baribir keladi, lekin klient uni ko'rsatmaydi).
    /// </summary>
    public bool WarnOnOverdue { get; set; } = true;

    /// <summary>
    /// Otsrochkadan keyin «faqat ko'rish» rejimi: sotuv va zakup qabuli
    /// bloklanadi, qolgan hamma narsa ishlaydi. O'chirilsa do'kon to'liq blok
    /// kunigacha odatdagidek ishlayveradi.
    /// </summary>
    public bool RestrictAfterGrace { get; set; } = true;

    /// <summary>
    /// Muddatdan keyin kirish butunlay yopiladigan kun. <c>0</c> = hech qachon.
    /// </summary>
    public int FullBlockAfterDays { get; set; } = 30;

    /// <summary>«Скоро срок» chegarasi (kun) — konsol ro'yxatlari uchun.</summary>
    public int SoonThresholdDays { get; set; } = 7;

    // ── Do'kon egasiga bildirishnomalar (Telegram) ──────────────────────────
    //
    // SMS ATAYLAB ishlatilmaydi: har xabar pul turadi, yetib borgani
    // tasdiqlanmaydi va parol kabi ma'lumot provayder jurnalida qolib ketadi.
    // Telegram kanali allaqachon qurilgan (bog'lanish bir martalik kod bilan
    // TASDIQLANGAN), bepul va ikki tomonlama — ega o'sha yerdan hisobot ham
    // so'ray oladi. Yagona sharti: ega Telegramni bog'lagan bo'lsin; kim
    // bog'lamagani konsolda ko'rinadi, ya'ni operator uni qo'ng'iroq bilan
    // xabardor qiladi (jimgina yo'qolmaydi).

    /// <summary>Obuna tugashiga <see cref="ExpiryReminderDays"/> kun qolganda egaga.</summary>
    public bool NotifyExpiring { get; set; } = true;

    /// <summary>Do'kon bloklanganda yoki «faqat ko'rish» rejimiga o'tganda.</summary>
    public bool NotifyBlocked { get; set; } = true;

    /// <summary>Obuna tugashidan necha kun oldin eslatiladi.</summary>
    public int ExpiryReminderDays { get; set; } = 3;

    // ── Qo'llab-quvvatlash (kirish sahifasida ko'rinadi) ────────────────────

    public string? SupportPhone { get; set; }
    public string? SupportTelegram { get; set; }
    public string? SupportEmail { get; set; }

    public DateTime UpdatedAt { get; set; }
}
