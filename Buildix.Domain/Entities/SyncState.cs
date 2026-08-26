namespace Buildix.Domain.Entities;

/// <summary>
/// Do'kon nusxasining sinxronizatsiya holati.
///
/// <para><b>Nega faylda emas, bazada.</b> Suv belgisi faqat ma'lumot
/// HAQIQATAN yozilgan bo'lsa oldinga surilishi kerak. Baza ichida bu bitta
/// tranzaksiya: yozuvlar va belgi birga saqlanadi yoki birga bekor bo'ladi.
/// Fayl bo'lsa, oraliqda uzilish yuz berganda belgi oldinga surilib,
/// ma'lumot esa yozilmay qolishi mumkin edi — o'sha o'zgarishlar do'konga
/// hech qachon yetib bormasdi va buni hech kim sezmasdi.</para>
///
/// <para>Do'konda bitta market bo'ladi, lekin kalit sifatida <c>MarketId</c>
/// ishlatiladi: bulut bazasida ham shu jadval bor va u yerda har do'konning
/// o'z qatori bo'lishi mumkin.</para>
/// </summary>
public class SyncState
{
    public int MarketId { get; set; }

    /// <summary>
    /// Bulutdan oxirgi olingan o'zgarish vaqti. Keyingi so'rovda aynan shu
    /// qiymat yuboriladi.
    /// </summary>
    public DateTimeOffset PullWatermark { get; set; }

    /// <summary>Oxirgi muvaffaqiyatli tortish (do'kon soati bo'yicha).</summary>
    public DateTime? LastPulledAtUtc { get; set; }

    /// <summary>
    /// Oxirgi urinishdagi xato. Muvaffaqiyatda tozalanadi.
    ///
    /// <para>Bu maydon ekranda «bulut bilan aloqa yo'q» belgisining manbai
    /// bo'ladi. Busiz sinxronizatsiya jimgina to'xtab qolar va do'kon buni
    /// faqat egasi telefonda eski raqamlarni ko'rganda bilardi.</para>
    /// </summary>
    public string? LastError { get; set; }
}
