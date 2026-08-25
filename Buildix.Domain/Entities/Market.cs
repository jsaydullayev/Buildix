using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Market/Tenant - alohida biznes egasi
/// </summary>
public class Market : IUpdateTracked
{
    public int Id { get; set; }  // Primary Key - int (auto-increment)
    public string Name { get; set; } = string.Empty;
    public string? Subdomain { get; set; }  // market1.example.com
    public string? Description { get; set; }

    /// <summary>
    /// Shahar — konsoldagi do'konlar ro'yxatida nom ostida ko'rsatiladi va
    /// qidiruvga kiradi. MarketSettings.Address to'liq manzil (chek uchun),
    /// bu esa faqat shahar: operator «Samarqanddagi do'konlar» ni bir qarashda
    /// ajratsin.
    /// </summary>
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }  // Subscription uchun

    /// <summary>
    /// Obuna tarifi. Narxi va limitlari <see cref="PlatformPlan"/> da —
    /// bu yerda faqat qaysi tarifda ekani. Default: eng past tarif.
    /// </summary>
    public Enums.PlanCode Plan { get; set; } = Enums.PlanCode.Start;

    /// <summary>
    /// Obunani yangilash eslatmasi qaysi muddat uchun yuborilgani. Stamp
    /// <see cref="ExpiresAt"/> ning O'ZI: to'lov qilinib muddat surilgach
    /// qiymat mos kelmay qoladi va keyingi davr uchun eslatma yana ketadi,
    /// bir davr ichida esa takrorlanmaydi.
    /// </summary>
    public DateTime? RenewalReminderSentFor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Oxirgi o'zgarish vaqti — <c>AppDbContext.SaveChanges</c> qo'yadi.
    /// Sabab va istisnolar: <see cref="IUpdateTracked"/>.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Block state (operational, reversible) ────────────────────────────
    // Separate from IsActive (which is the soft-delete flag set by
    // DeleteOwner). A blocked market still exists and can be restored — it
    // simply rejects all authentication and tenant resolution attempts.
    // Typical use: subscription payment lapsed.
    public bool IsBlocked { get; set; } = false;
    public DateTime? BlockedAt { get; set; }
    public string? BlockedReason { get; set; }
    public Guid? BlockedByUserId { get; set; }

    // Owner who created this market
    public Guid OwnerId { get; set; }

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Zakup> Zakups { get; set; } = new List<Zakup>();
    public ICollection<Debt> Debts { get; set; } = new List<Debt>();
    public CashRegister? CashRegister { get; set; }

    // ── Subscription rule (single source of truth) ───────────────────────
    // The whole platform gates a tenant's "front door" (subdomain login +
    // real-time request access) on these two methods so the definition of
    // "open" never drifts between the login path, the middleware and the
    // public market-state endpoint. Time is always UTC (compare against
    // DateTime.UtcNow / ITashkentClock.UtcNow).

    /// <summary>
    /// Subscription has a set end date that has passed. Blocked/soft-deleted
    /// markets are handled by their own gates (IsBlocked → 423, !IsActive →
    /// not-found) so this returns false for them — an expired-but-blocked
    /// market surfaces as "blocked", not "expired". A null <see cref="ExpiresAt"/>
    /// means "no expiry set" (grandfathered / unlimited) and is never expired.
    /// </summary>
    public bool IsSubscriptionExpired(DateTime nowUtc) =>
        IsActive && !IsBlocked && ExpiresAt.HasValue && ExpiresAt.Value <= nowUtc;

    /// <summary>
    /// The market's login door is open: not blocked, not soft-deleted, and
    /// either no expiry is set or it is still in the future.
    /// </summary>
    public bool IsSubscriptionActive(DateTime nowUtc) =>
        IsActive && !IsBlocked && (!ExpiresAt.HasValue || ExpiresAt.Value > nowUtc);

    /// <summary>
    /// Obuna eshigining TO'LIQ holati — bitta manba. Muddat tugagani darhol
    /// «yopiq» degani emas: platforma sozlamalarida otsrochka (grace) va to'liq
    /// blokgacha bo'lgan muddat bor.
    ///
    /// <para>Bosqichlar (dizayn: «Правила блокировки»):</para>
    /// <list type="bullet">
    ///   <item><b>Active</b> — muddat bor yoki kelajakda.</item>
    ///   <item><b>Overdue</b> — muddat o'tdi, otsrochka ichida: hamma narsa
    ///   ishlaydi, foydalanuvchiga sariq plashka ko'rsatiladi.</item>
    ///   <item><b>Restricted</b> — otsrochka tugadi: ma'lumot O'QILADI, lekin
    ///   pul harakati (sotuv, zakup qabuli) to'xtaydi.</item>
    ///   <item><b>Blocked</b> — qo'lda blok yoki to'liq blok muddati o'tdi:
    ///   kirish umuman yopiq.</item>
    /// </list>
    ///
    /// <para>Login, middleware va public state endpoint AYNAN shu metodni
    /// chaqiradi — aks holda «yopiq»ning ta'rifi uch joyda uch xil bo'lib
    /// ketardi.</para>
    /// </summary>
    /// <param name="graceDays">Muddat tugagach xizmat ochiq qoladigan kunlar.</param>
    /// <param name="fullBlockAfterDays">
    /// Muddatdan keyin kirish butunlay yopiladigan kun. <c>0</c> = hech qachon
    /// (faqat <c>Restricted</c> da qoladi).
    /// </param>
    public SubscriptionState EvaluateSubscription(DateTime nowUtc, int graceDays, int fullBlockAfterDays)
    {
        // Soft-delete va qo'lda blok — obuna hisobidan oldin: ular boshqa
        // sabab va boshqa ekran (423), obuna esa 402.
        if (!IsActive || IsBlocked) return SubscriptionState.Blocked;
        if (!ExpiresAt.HasValue || ExpiresAt.Value > nowUtc) return SubscriptionState.Active;

        var daysOverdue = (nowUtc - ExpiresAt.Value).TotalDays;
        if (daysOverdue <= graceDays) return SubscriptionState.Overdue;
        if (fullBlockAfterDays > 0 && daysOverdue > fullBlockAfterDays) return SubscriptionState.Blocked;
        return SubscriptionState.Restricted;
    }
}
