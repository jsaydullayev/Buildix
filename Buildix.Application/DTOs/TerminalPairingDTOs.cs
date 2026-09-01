using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>Panelda ko'rsatiladigan kod.</summary>
public record PairingCodeDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("expiresAtUtc")] DateTime ExpiresAtUtc,
    [property: JsonPropertyName("marketName")] string MarketName);

/// <summary>
/// Bog'lanish natijasi.
///
/// <para><b>Kalit shu yerda BIR MARTA qaytariladi.</b> Bulutda faqat uning
/// hash'i qoladi, ya'ni yo'qolsa qayta olib bo'lmaydi — kompyuterni qaytadan
/// bog'lash kerak bo'ladi. Bu ataylab shunday: kalitni keyin ham so'rab
/// olish mumkin bo'lsa, bulut bazasiga kirgan odam do'kon nomidan gapira
/// olardi.</para>
/// </summary>
public record PairedTerminalDto(
    [property: JsonPropertyName("terminalId")] Guid TerminalId,
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("marketName")] string MarketName,
    [property: JsonPropertyName("key")] string Key);

/// <summary>Ilova yuboradigan so'rov.</summary>
public record RedeemPairingRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("terminalName")] string TerminalName);

/// <summary>
/// Do'kon egasining login-paroli bilan bog'lash so'rovi — kodsiz asosiy yo'l.
///
/// <para><c>Subdomain</c> ixtiyoriy: bir xil login turli do'konlarda
/// uchrashi mumkin va o'shanda do'konni ko'rsatish kerak bo'ladi.</para>
/// </summary>
public record ActivateTerminalRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("terminalName")] string TerminalName,

    /// <summary>
    /// Do'konda allaqachon bog'langan kompyuter bo'lsa, uni BEKOR QILIB
    /// shu kompyuterni bog'lash.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kerak.</b> Server kompyuter qayta o'rnatilsa (disk
    /// tozalandi, Windows qaytadan qo'yildi, kompyuter almashtirildi) yangi
    /// nusxada hech qanday kalit qolmaydi, bulutda esa ESKI yozuv tirik
    /// turadi. Natijada do'kon butunlay qulflanardi: bog'lash «allaqachon
    /// bog'langan» deb rad etar, bekor qilinishi kerak bo'lgan kompyuter esa
    /// endi mavjud emas edi.</para>
    ///
    /// <para><b>Nega sukut bo'yicha o'chiq.</b> Eski kompyuterda bulutga hali
    /// yuborilmagan savdolar qolgan bo'lishi mumkin va bekor qilingandan
    /// keyin ular hech qachon jo'natilmaydi. Shuning uchun bu jimgina
    /// bo'lmaydi — egasi ogohlantirishni o'qib, ataylab tasdiqlaydi.</para>
    ///
    /// <para>Ruxsat egalikka tayanadi: bu yo'l do'kon EGASINING
    /// login-parolini talab qiladi, ya'ni tasdiqni faqat do'konni
    /// boshqarishga haqli odam bera oladi.</para>
    /// </remarks>
    [property: JsonPropertyName("replaceExisting")] bool ReplaceExisting = false);

/// <summary>
/// Do'kon dasturini yuklab olish uchun kerakli ma'lumot.
///
/// <para><c>Url</c> bo'sh bo'lsa paket hali serverga qo'yilmagan.</para>
/// </summary>
public record DesktopAppDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("version")] string? Version);

/// <summary>Panel uchun: do'konga bog'langan kompyuter.</summary>
public record TerminalDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pairedAt")] DateTime PairedAt,
    [property: JsonPropertyName("lastSeenAtUtc")] DateTime? LastSeenAtUtc,
    [property: JsonPropertyName("revokedAtUtc")] DateTime? RevokedAtUtc,
    [property: JsonPropertyName("lastIpAddress")] string? LastIpAddress);
