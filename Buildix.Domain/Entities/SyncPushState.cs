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

    /// <summary>
    /// Belgidagi vaqtga ega OXIRGI yozuvning kaliti.
    ///
    /// <para><b>Nega yolg'iz vaqt yetmaydi.</b> Bitta saqlashdagi yozuvlar
    /// AYNAN bir xil vaqt oladi — masalan Excel'dan 200 ta tovar import
    /// qilinganda. Agar butun paket shu bir xil vaqtga ega bo'lsa, faqat
    /// vaqtga tayangan belgi joyidan qimirlamas va do'kon o'sha 200 qatorni
    /// ABADIY qayta-qayta yuboraverardi: yangi savdolar esa navbatda turib
    /// qolardi. Kalit qo'shilganda tartib to'liq aniq bo'ladi va har paket
    /// oldinga siljiydi.</para>
    /// </summary>
    public Guid LastId { get; set; }

    /// <summary>Oxirgi muvaffaqiyatli yuborish (do'kon soati bo'yicha).</summary>
    public DateTime? LastPushedAtUtc { get; set; }
}
