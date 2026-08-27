using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

/// <summary>
/// Bulutga bog'langan do'kon kompyuteri.
///
/// <para><b>Nima uchun kerak.</b> Do'kon ilovasi bulut bilan foydalanuvchi
/// nomidan emas, O'ZI gaplashadi: sinxronizatsiya kechasi ham, kassir
/// chiqib ketgandan keyin ham ishlashi kerak. Ya'ni unga odamnikidan alohida,
/// o'ziga tegishli kalit kerak. Har kompyuter alohida yozuv bo'lgani uchun
/// bittasi yo'qolsa (o'g'irlangan noutbuk) faqat o'shanisi bekor qilinadi,
/// qolgan kassalar ishlayveradi.</para>
///
/// <para><b>Kalit bu yerda SAQLANMAYDI.</b> Faqat uning hash'i yotadi —
/// aynan parol kabi. Bulut bazasiga kirgan odam ham do'kon nomidan
/// gapira olmasligi kerak.</para>
///
/// <para><b>Nega sekin hash emas.</b> Parollar uchun bcrypt/argon kerak,
/// chunki odam o'ylab topgan parol zaif va uni taxmin qilib bo'ladi. Bu
/// kalit esa 32 baytlik tasodifiy qiymat — uni taxmin qilishning imkoni yo'q,
/// shuning uchun SHA-256 yetarli va u har so'rovda tez ishlaydi.</para>
/// </summary>
public class ShopTerminal : BaseEntity
{
    public int MarketId { get; set; }

    /// <summary>Texnik bergan nom: «Server kassa», «2-kassa».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Kalitning SHA-256 hash'i (hex). Kalitning o'zi hech qayerda saqlanmaydi.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// Oxirgi aloqa vaqti. Egasiga «do'kon uch kundan beri aloqaga
    /// chiqmayapti» deyish uchun yagona manba.
    /// </summary>
    public DateTime? LastSeenAtUtc { get; set; }

    /// <summary>
    /// Do'kondan ma'lumot oxirgi marta QACHON KELGANI (muvaffaqiyatli push).
    /// </summary>
    /// <remarks>
    /// <para><b>Nega <see cref="LastSeenAtUtc"/> yetarli emas.</b> U so'rov
    /// KELGANIDA, ya'ni kalit tekshiruvidan o'tganda qo'yiladi — push'ning
    /// o'zi muvaffaqiyatli bo'ldimi yoki tashqi kalit xatosi bilan yiqildimi,
    /// unga farqi yo'q. Natijada bulut har daqiqada «do'kon aloqada» deb
    /// belgilar, ma'lumot esa haftalab kelmasligi mumkin edi va egasining
    /// telefonida yashil «hozirgina sinxron» yozuvi turardi.</para>
    ///
    /// <para>«Ma'lumot yangi» degan savolga faqat SHU maydon javob bera
    /// oladi: aloqa boshqa narsa, ma'lumotning yetib kelishi boshqa.</para>
    /// </remarks>
    public DateTime? LastPushAtUtc { get; set; }

    /// <summary>
    /// Bekor qilingan vaqt. Null bo'lmasa kalit ishlamaydi — kompyuter
    /// almashtirilganda yoki yo'qolganda SuperAdmin panelidan qo'yiladi.
    /// </summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Egasiga «do'kon aloqada emas» xabari oxirgi marta qachon yuborilgani.
    ///
    /// <para><b>Nega bazada, xotirada emas.</b> Birinchi variantda bu
    /// ma'lumot fon xizmatining xotirasida turardi va API har qayta ishga
    /// tushganda unutilardi — ya'ni har yangilanish yoki qayta ishga tushirish
    /// egasiga o'sha xabarni QAYTA yuborardi. Bir necha takrordan keyin u
    /// bildirishnomalarni o'chirib qo'yadi va haqiqiy nosozlikni ham
    /// ko'rmaydi.</para>
    /// </summary>
    public DateTime? LastOfflineAlertAtUtc { get; set; }

    /// <summary>Oxirgi ko'rilgan IP — shubhali holatni aniqlash uchun.</summary>
    public string? LastIpAddress { get; set; }

    public Market? Market { get; set; }
}
