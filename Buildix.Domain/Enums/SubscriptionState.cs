namespace Buildix.Domain.Enums;

/// <summary>
/// Do'konning obuna eshigi qaysi bosqichda. Faqat hisoblanadi
/// (<see cref="Entities.Market.EvaluateSubscription"/>) — DB'da saqlanmaydi,
/// shuning uchun «eskirgan holat» degan muammo yo'q.
/// </summary>
public enum SubscriptionState
{
    /// <summary>To'langan yoki muddatsiz — hech qanday cheklov yo'q.</summary>
    Active = 0,

    /// <summary>
    /// Muddat o'tdi, lekin otsrochka ichida: ish to'xtamaydi, faqat
    /// ogohlantirish ko'rsatiladi.
    /// </summary>
    Overdue = 1,

    /// <summary>
    /// Otsrochka tugadi: ma'lumot ko'rinadi, lekin pul harakati (sotuv,
    /// zakup qabuli) bloklanadi.
    /// </summary>
    Restricted = 2,

    /// <summary>Kirish umuman yopiq (qo'lda blok yoki to'liq blok muddati).</summary>
    Blocked = 3
}
