using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

/// <summary>
/// Botning bir martalik bog'lanish kodi — «bu Telegram akkaunt haqiqatan ham
/// meniki» degan yagona isbot.
///
/// <para><b>Nima uchun kerak.</b> Ilgari foydalanuvchi Account ekranida xom
/// Telegram ID yozardi va uning o'sha akkauntga egaligini HECH NARSA
/// tekshirmasdi. Bot esa foydalanuvchini aynan chat ID bo'yicha taniydi — ya'ni
/// begona raqamni yozib qo'yish o'sha begona odamga bot orqali do'kon
/// ma'lumotini (cheklar, faktura PDF, qoldiq ro'yxati) ochib berardi. Kod esa
/// teskari yo'nalishda ishlaydi: uni faqat CHAT egasi ko'radi, demak koddan
/// olingan <see cref="ChatId"/> isbotlangan hisoblanadi.</para>
///
/// <para><b>Hayot sikli.</b> Bog'lanmagan chat botga yozadi → kod beriladi
/// (10 daqiqa). Panelda kod kiritiladi → <see cref="UsedAtUtc"/> qo'yiladi va
/// qator boshqa hech qachon ishlamaydi. Ishlatilmagan-muddati o'tgan qatorlar
/// keyingi kod so'ralganda tozalanadi.</para>
///
/// <para>Qator marketga bog'lanmagan (kod berilganda chat qaysi do'konga
/// tegishli ekani hali noma'lum), shuning uchun tenant query-filter bu
/// jadvalga QO'LLANMAYDI.</para>
/// </summary>
public class TelegramLinkCode : BaseEntity
{
    /// <summary>6 xonali kod. Ishlatilmagan qatorlar orasida unikal (qisman indeks).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Kod yuborilgan Telegram chat — bog'lanishning isbotlangan manbasi.</summary>
    public long ChatId { get; set; }

    /// <summary>Amal qilish muddati (UTC).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Ishlatilgan vaqt (UTC). Null = hali ishlatilmagan.</summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>Kodni ishlatgan foydalanuvchi — audit uchun.</summary>
    public Guid? UsedByUserId { get; set; }
}
