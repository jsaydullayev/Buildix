using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

/// <summary>
/// Do'konni bulutga bog'lash uchun bir martalik kod.
///
/// <para><b>Nima uchun kod, kalitning o'zi emas.</b> Kalit 32 baytlik
/// tasodifiy qiymat — uni telefonda aytib bo'lmaydi, qo'lda ko'chirishda esa
/// xato qilinadi. Kod esa qisqa va odam o'qiy oladigan; u kalitni OLIB
/// KELADI, o'zi kalit emas. Bir marta ishlatilgach o'ladi, ya'ni kimdir
/// ko'rib qolgan bo'lsa ham keyin foyda bermaydi.</para>
///
/// <para><b>Alifboda 0, O, 1, I, L YO'Q.</b> Kodni telefon orqali aytishadi
/// va qog'ozdan ko'chirishadi — bu belgilar aynan shu paytda adashtiradi.
/// Sakkiz belgi 32 harfli alifbodan — taxmin qilib topish uchun juda ko'p,
/// ayniqsa urinishlar soni cheklangan va kod bir sutkada o'ladi.</para>
///
/// <para>Qator marketga bog'langan: kod aynan qaysi do'konga tegishli ekani
/// oldindan ma'lum. Lekin uni ishlatadigan tomon (yangi o'rnatilgan ilova)
/// hali autentifikatsiyadan o'tmagan, shuning uchun tenant filtri bu
/// jadvalga QO'LLANMAYDI — aks holda kodni tekshirib bo'lmasdi.</para>
/// </summary>
public class TerminalPairingCode : BaseEntity
{
    /// <summary>Ko'rinishi: BX-4K7P-92MC.</summary>
    public string Code { get; set; } = string.Empty;

    public int MarketId { get; set; }

    /// <summary>Amal qilish muddati (UTC). Odatda bir sutka.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Ishlatilgan vaqt (UTC). Null = hali ishlatilmagan.</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Kod qaysi kompyuterga aylandi — audit uchun.</summary>
    public Guid? UsedByTerminalId { get; set; }

    /// <summary>Kodni bergan xodim.</summary>
    public Guid CreatedByUserId { get; set; }

    public Market? Market { get; set; }
}
