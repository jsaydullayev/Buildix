using System.Text;

namespace Buildix.Application.Common;

/// <summary>
/// Do'kon NOMIDAN DNS-xavfsiz sub-path (slug) yasaydi: «Тош Кон Строй Маркет»
/// → <c>tosh-kon-stroy-market</c>.
///
/// <para><b>Nima uchun nomdan, login'dan emas.</b> Sub-path — mijoz ko'radigan
/// va yozib qo'yadigan manzil (<c>buildix.uz/tosh-kon-stroy-market/login</c>).
/// Ilgari u <c>username</c> dan yasalardi va oxiriga tasodifiy 6 belgi
/// qo'shilardi (<c>sardora3f19c</c>) — bu manzil do'kon nomi bilan hech qanday
/// aloqasi bo'lmagan, aytib bo'lmaydigan va eslab qolinmaydigan qatorga
/// aylanardi.</para>
///
/// <para><b>Nega translitatsiya kerak.</b> Do'kon nomlari kirillcha
/// («Стройбаза №1»), sub-path esa faqat <c>[a-z0-9-]</c> bo'lishi shart.
/// Eski kod <c>char.IsLetterOrDigit</c> bilan filtrlardi — bu kirill harflarni
/// HAM o'tkazib yuborardi, ya'ni kirillcha login uchun DNS-yaroqsiz sub-path
/// yozilib ketardi (avtomatik yo'l format tekshiruvidan o'tmaydi).</para>
/// </summary>
public static class SubdomainSlug
{
    /// <summary>Slug uzunligi chegarasi — DNS 63 ga ruxsat beradi, lekin manzil qo'l bilan yoziladi.</summary>
    private const int MaxLength = 40;
    private const int MinLength = 3;

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e",
        ['ё'] = "yo", ['ж'] = "j", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k",
        ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "x", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "", ['ы'] = "i", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
        // O'zbek kirill qo'shimchalari.
        ['қ'] = "q", ['ғ'] = "g", ['ў'] = "o", ['ҳ'] = "h",
    };

    /// <summary>
    /// Nomdan slug yasaydi. Natija DNS naqshiga (<c>^[a-z0-9]([a-z0-9-]*[a-z0-9])?$</c>)
    /// kafolatli mos keladi. Nomda lotin/kirill harf umuman bo'lmasa
    /// (masalan faqat ieroglif) — <paramref name="fallback"/> ishlatiladi,
    /// u ham yaramasa <c>"market"</c>.
    /// </summary>
    public static string From(string? name, string? fallback = null)
    {
        var slug = Build(name);
        if (slug.Length >= MinLength) return slug;

        slug = Build(fallback);
        if (slug.Length >= MinLength) return slug;

        return "market";
    }

    private static string Build(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
            else if (Translit.TryGetValue(ch, out var latin))
                sb.Append(latin);
            else
                // «», №, bo'shliq, tinish — hammasi ajratuvchi. Ketma-ket
                // ajratuvchilar pastda bitta '-' ga siqiladi.
                sb.Append('-');
        }

        // Ketma-ket defislarni siqish + chetlaridan olib tashlash: DNS naqshi
        // boshi va oxirida faqat harf/raqamga ruxsat beradi.
        var parts = sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join('-', parts);
        if (joined.Length <= MaxLength) return joined;

        // Kesishda so'z o'rtasidan bo'lib qo'ymaslik — oxirgi to'liq bo'lakkacha.
        var cut = joined[..MaxLength];
        var lastDash = cut.LastIndexOf('-');
        if (lastDash >= MinLength) cut = cut[..lastDash];
        return cut.TrimEnd('-');
    }
}
