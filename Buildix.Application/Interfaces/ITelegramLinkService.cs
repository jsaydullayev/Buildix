using Buildix.Domain.Entities;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Telegram akkauntni Buildix hisobiga BIR MARTALIK KOD orqali bog'lash.
///
/// <para>Ikki tomon: botga yozgan bog'lanmagan chat koddan nusxa oladi
/// (<see cref="IssueCodeAsync"/>), foydalanuvchi esa o'sha kodni panelda
/// kiritadi (<see cref="ConsumeAsync"/>). Chat ID hech qachon qo'lda
/// kiritilmaydi — u faqat koddan olinadi, shuning uchun bog'lanish
/// isbotlangan hisoblanadi.</para>
/// </summary>
public interface ITelegramLinkService
{
    /// <summary>
    /// Chat uchun amaldagi kodni qaytaradi, bo'lmasa yangisini yaratadi.
    /// Takroriy so'rovda AYNAN o'sha kod qaytadi — bir chatga o'nlab kod
    /// tarqatilib, foydalanuvchi qaysi biri amalda ekanini chalkashtirmasin.
    /// O'zgarishlar shu yerda saqlanadi (bot yo'li o'z tranzaksiyasiga ega emas).
    /// </summary>
    Task<(string Code, DateTime ExpiresAtUtc)> IssueCodeAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kodni ishlatadi va isbotlangan chat ID ni qaytaradi. Qator "ishlatilgan"
    /// deb BELGILANADI, lekin SAQLANMAYDI — chaqiruvchi uni foydalanuvchi
    /// qatori bilan bitta <c>SaveChangesAsync</c> ichida yozadi, aks holda
    /// bog'lanish yarim holatda qolishi mumkin edi.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Kod formati noto'g'ri, topilmadi/muddati o'tgan/ishlatilgan, urinishlar
    /// chegarasi tugagan yoki chat boshqa foydalanuvchiga bog'langan.
    /// Xabar matni to'g'ridan-to'g'ri foydalanuvchiga ko'rsatiladi.
    /// </exception>
    Task<long> ConsumeAsync(User user, string code, CancellationToken cancellationToken = default);
}
