namespace Buildix.Domain.Entities;

/// <summary>
/// Har bir jadval uchun alohida yuborish belgisi.
///
/// <para><b>Nega jadval bo'yicha, yagona emas.</b> Bitta so'rovda yuboriladigan
/// yozuvlar soni cheklangan — aks holda kun bo'yi to'plangan ma'lumot bitta
/// ulkan so'rovga aylanar va sekin internetda hech qachon o'tmasdi. Yagona
/// belgi bo'lsa, chegaraga urilgan jadval qolganlarini ham to'sib qo'yardi:
/// belgi eng «orqada qolgan» jadval bo'yicha suriladi va boshqa jadvallar
/// o'z yozuvlarini qayta-qayta yuboraverardi.</para>
///
/// <para>Alohida belgi bilan har jadval o'z tezligida oldinga siljiydi.</para>
/// </summary>
public class SyncPushState
{
    public int MarketId { get; set; }

    /// <summary>Entity nomi — <c>Sale</c>, <c>Product</c> va hokazo.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>Shu jadvaldan bulutga yetkazilgan oxirgi o'zgarish vaqti.</summary>
    public DateTimeOffset Watermark { get; set; }

    /// <summary>Oxirgi muvaffaqiyatli yuborish (do'kon soati bo'yicha).</summary>
    public DateTime? LastPushedAtUtc { get; set; }
}
