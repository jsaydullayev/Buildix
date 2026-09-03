namespace Buildix.Domain.Entities;

/// <summary>
/// «Bu qator BULUTDAN keldi» belgisi — qaytib yuqoriga ketmasligi uchun.
/// </summary>
/// <remarks>
/// <para><b>Qanday aylanish tug'iladi.</b> Yuborish xizmati har jadvalning
/// suv belgisidan yuqoridagi HAMMA qatorini yig'adi. Bulutdan tushgan qator
/// do'kon bazasiga yozilganda o'zining lokal <c>UpdatedAt</c> ini oladi —
/// ya'ni u darhol «yangi o'zgargan» bo'lib ko'rinadi va keyingi yuborishda
/// bulutga qaytib ketadi. Bulut uni qabul qilib o'z vaqtini qo'yadi,
/// ikkinchi kassa uni yana tortadi, yana yozadi, yana yuboradi —
/// <b>cheksiz aylanish</b>. Hech qanday xato chiqmaydi: shunchaki tarmoq va
/// baza bekorga ishlaydi, qatorlar esa tinimsiz qayta yoziladi.</para>
///
/// <para><b>Nega oddiy «bulutdan keldi» bayrog'i yetmaydi.</b> Bulutdan
/// kelgan chekni keyinchalik SHU kassa o'zgartirishi mumkin — masalan
/// boshqa kassada yozilgan qarzni undirsa. Bunday o'zgarish bulutga
/// CHIQISHI shart. Shuning uchun belgi qatorning qaysi HOLATI kelganini
/// eslab qoladi: <see cref="AppliedUpdatedAt"/> qatorning joriy
/// <c>UpdatedAt</c> iga teng bo'lsa — qator tegilmagan, yuborilmaydi.
/// Teng bo'lmasa — do'konda o'zgargan, demak yuboriladi.</para>
///
/// <para>Bu jadval FAQAT do'kon nusxasida to'ladi; bulutda u bo'sh
/// qolaveradi.</para>
/// </remarks>
public class SyncedRowMark
{
    /// <summary>Qator kaliti — GUID barcha jadvallarda takrorlanmas.</summary>
    public Guid RowId { get; set; }

    /// <summary>Entity nomi — <c>Sale</c>, <c>Payment</c> va hokazo.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Qator qo'llanganda bazaga tushgan <c>UpdatedAt</c>.
    ///
    /// <para>Solishtiruv nuqtasi: qatorning joriy qiymati shundan farq qilsa,
    /// u do'konda o'zgargan va bulutga yuborilishi kerak.</para>
    /// </summary>
    public DateTime AppliedUpdatedAt { get; set; }
}
