namespace Buildix.Application.Common;

/// <summary>
/// Uch tildagi bitta matn.
/// </summary>
/// <remarks>
/// <para><b>Nega kerak.</b> Excel eksportlari sarlavhalarni anonim tipning
/// XOSSA NOMIDAN olardi, ya'ni har til uchun butun proyeksiya qaytadan
/// yozilardi. Ikki nusxa bor edi — o'zbekcha va ruscha — va inglizcha
/// jimgina o'zbekchaga tushardi: ilova ingliz tilida tursa ham fayl
/// o'zbekcha chiqardi va buni hech qanday xato ko'rsatmasdi.</para>
///
/// <para>Endi matn bir joyda uchala tilda turadi va til faqat oxirida
/// tanlanadi. Yangi til qo'shish — bitta maydon, proyeksiyani nusxalash
/// emas.</para>
/// </remarks>
public readonly record struct Localized(string Uz, string Ru, string En)
{
    /// <summary>Tilga mos matn. Noma'lum til — o'zbekcha (do'kon tili).</summary>
    public string For(string? lang) => lang?.Trim().ToLowerInvariant() switch
    {
        "ru" => Ru,
        "en" => En,
        _ => Uz,
    };
}
