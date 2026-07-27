using Buildix.Application.Common;

namespace Buildix.Tests;

/// <summary>
/// Sub-path do'kon NOMIDAN yasaladi (ilgari login'dan + tasodifiy 6 belgi).
/// Manzilni mijoz o'qiydi va yozadi, shuning uchun natija ikki shartga
/// bo'ysunishi shart: DNS naqshiga mos va nomdan tanib olinadigan.
/// </summary>
public class SubdomainSlugTests
{
    // DNS: faqat kichik lotin/raqam/defis, chetlarida defis yo'q, 3–63 belgi.
    private static void AssertDnsSafe(string slug)
    {
        Assert.InRange(slug.Length, 3, 63);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.StartsWith('-') || slug.EndsWith('-'), $"chetida defis: {slug}");
        Assert.All(slug, ch => Assert.True(
            ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-',
            $"DNS uchun yaroqsiz belgi '{ch}' ({slug})"));
    }

    [Theory]
    [InlineData("«Тош Кон Строй Маркет»", "tosh-kon-stroy-market")]
    [InlineData("Стройбаза №1", "stroybaza-1")]
    [InlineData("«Барака Строй»", "baraka-stroy")]
    [InlineData("«Олтин Гишт»", "oltin-gisht")]
    [InlineData("Sardor Market", "sardor-market")]
    [InlineData("  Евро   Дом  ", "evro-dom")]
    public void Cyrillic_market_names_transliterate_to_a_readable_slug(string name, string expected)
    {
        var slug = SubdomainSlug.From(name);
        Assert.Equal(expected, slug);
        AssertDnsSafe(slug);
    }

    [Fact]
    public void Uzbek_cyrillic_letters_map_to_their_latin_equivalents()
    {
        // қ→q, ғ→g, ў→o, ҳ→h — o'zbek kirillida bor, ruschada yo'q.
        Assert.Equal("qishloq-gozal-hovli", SubdomainSlug.From("Қишлоқ Гўзал Ҳовли"));
    }

    [Fact]
    public void Punctuation_never_leaks_into_the_slug()
    {
        // Eski kod char.IsLetterOrDigit bilan filtrlardi — kirill harflar
        // O'TIB KETARDI va DNS uchun yaroqsiz sub-path yozilardi.
        var slug = SubdomainSlug.From("ООО «Мега/Строй» — Инвест, №7!");
        AssertDnsSafe(slug);
        Assert.Equal("ooo-mega-stroy-invest-7", slug);
    }

    [Fact]
    public void Long_names_are_cut_on_a_word_boundary()
    {
        var slug = SubdomainSlug.From("Самый Длинный Строительный Гипермаркет Республики Узбекистан");
        AssertDnsSafe(slug);
        Assert.True(slug.Length <= 40, $"uzunligi {slug.Length}: {slug}");
        // So'z o'rtasidan kesilmaydi — oxirgi bo'lak butun qoladi.
        Assert.DoesNotContain("-g", slug[^2..]);
        Assert.StartsWith("samiy-dlinniy-stroitelniy", slug);
    }

    [Fact]
    public void Falls_back_to_the_username_then_to_a_constant()
    {
        // Nomda tanib bo'ladigan harf yo'q → login; u ham yaramasa "market".
        Assert.Equal("sardor", SubdomainSlug.From("!!! ???", "sardor"));
        Assert.Equal("market", SubdomainSlug.From("!!!", "??"));
        Assert.Equal("market", SubdomainSlug.From(null, null));
    }

    [Fact]
    public void Short_but_valid_names_survive()
    {
        Assert.Equal("abc", SubdomainSlug.From("ABC"));
        // 2 belgi DNS minimumidan qisqa → fallback ishlaydi.
        Assert.Equal("sardor", SubdomainSlug.From("АБ", "sardor"));
    }
}
