namespace Buildix.Domain.Common;

/// <summary>
/// Yozuv oxirgi marta qachon o'zgargani. Bulut bilan sinxronizatsiya aynan shu
/// maydonga suyanadi: «oxirgi aloqadan keyin nima o'zgardi» degan savolga
/// javob beradigan boshqa mexanizm yo'q.
///
/// <para><b>Buni QO'LDA yozmang.</b> Qiymatni <c>AppDbContext.SaveChanges</c>
/// o'zi qo'yadi. Xizmatlarga ishonib bo'lmasligi tajribada ko'rindi: bu maydon
/// oldin faqat to'rt joyda o'rnatilardi va <c>ProductService</c> uni butunlay
/// unutgan edi — tovar narxi o'zgarsa ham <c>UpdatedAt</c> eski qolardi.
/// Bunday xato hech qanday belgi bermaydi: kod ishlaydi, testlar o'tadi, faqat
/// bulutdagi ma'lumot jimgina eskirib boradi.</para>
///
/// <para><b>Nima uchun hamma jadvalda emas.</b> Uchta jadval ataylab tashqarida:
/// <c>LoginAttempt</c>, <c>RevokedToken</c>, <c>IdempotencyRecord</c>. Ular
/// bulutga umuman yuborilmaydi va ikkitasi <c>ExecuteUpdate</c> bilan, ya'ni
/// EF kuzatuvchisini chetlab o'tib yoziladi — u yerdagi ustun yangilanmasdan
/// qolar va ishonchli ko'rinib turgani holda yolg'on ma'lumot berardi.</para>
/// </summary>
public interface IUpdateTracked
{
    /// <summary>UTC. Yozuv yaratilganda <see cref="DateTime"/> qiymati
    /// yaratilish vaqtiga teng bo'ladi.</summary>
    DateTime UpdatedAt { get; set; }
}
