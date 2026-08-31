namespace Buildix.Desktop;

/// <summary>
/// Do'konning veb-manzilini bulut manzili va do'kon belgisiga ajratadi.
///
/// <para><b>Nega kerak.</b> Har do'konning o'z manzili bor —
/// <c>buildix.uz/taxtapul</c>. Yo'lning birinchi bo'lagi (<c>taxtapul</c>)
/// aynan qaysi do'kon ekanini bildiradi.</para>
///
/// <para><b>Nima uchun bu MUHIM.</b> Bog'lanishda login va parol bulutga
/// yuboriladi. Do'kon ko'rsatilmasa, bulut foydalanuvchini BARCHA do'konlar
/// ichidan qidiradi. Ikki do'konda bir xil login bo'lsa — o'zbek do'konlarida
/// «admin», «jamshid» kabi loginlar tez-tez uchraydi — va parollari ham bir
/// xil bo'lsa, qaysi biri tanlanishi umuman aniqlanmagan bo'lardi: bazadan
/// birinchi qaytgani olinardi. Ya'ni kassa BOSHQA do'konga bog'lanib
/// ketishi mumkin edi. Do'kon belgisi bu ehtimolni butunlay yo'q qiladi.</para>
/// </summary>
internal static class MarketAddress
{
    /// <summary>Ajratish natijasi.</summary>
    /// <param name="CloudUrl">Bulut manzili — <c>https://buildix.uz</c>.</param>
    /// <param name="Subdomain">Do'kon belgisi — <c>taxtapul</c>; ko'rsatilmagan bo'lsa <c>null</c>.</param>
    internal sealed record Parts(string CloudUrl, string? Subdomain);

    /// <summary>
    /// Kiritilgan matnni ajratadi; manzil yaroqsiz bo'lsa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>Sxema yozilmasa <c>https://</c> qo'yiladi: egasi manzilni
    /// brauzerdan ko'chirganda odatda <c>buildix.uz/taxtapul</c> shaklida
    /// oladi va uni «noto'g'ri» deb rad etish faqat xalaqit berardi.</para>
    ///
    /// <para>Faqat BIRINCHI bo'lak olinadi: egasi butun sahifa manzilini
    /// (<c>buildix.uz/taxtapul/desktop</c>) ko'chirishi tabiiy va u ham
    /// ishlashi kerak.</para>
    /// </remarks>
    internal static Parts? Parse(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var cloud = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

        var first = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return new Parts(cloud, Normalize(first));
    }

    /// <summary>
    /// Do'kon belgisini tozalaydi; belgi bo'lmasa <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Bulut belgilarni kichik harfda saqlaydi va solishtiruv ham shunday
    /// (<c>AuthService</c> uni <c>ToLowerInvariant</c> qiladi), shuning
    /// uchun bu yerda ham kichiklashtiramiz — egasi «Taxtapul» deb yozsa
    /// ham topilsin.
    /// </remarks>
    private static string? Normalize(string? segment)
    {
        var slug = segment?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(slug) ? null : slug;
    }
}
