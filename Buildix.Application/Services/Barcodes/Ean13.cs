using System.Security.Cryptography;

namespace Buildix.Application.Services.Barcodes;

/// <summary>
/// Do'kon o'zi chiqaradigan EAN-13 kodlari.
///
/// <para><b>Nega EAN-13 va nega «20» bilan boshlanadi.</b> EAN-13 raqamlar
/// makonining <c>20…29</c> bilan boshlanadigan qismi xalqaro miqyosda
/// «restricted distribution» — ya'ni do'kon ichki ehtiyoji uchun ajratilgan va
/// GS1 uni hech qachon hech kimga bermaydi. Demak bu yerda yaratilgan kod
/// haqiqiy zavod kodi bilan hech qachon to'qnashmaydi. O'zboshimchalik bilan
/// boshqa prefiks tanlansa (masalan «46»), bir kun kelib xuddi shu kodli
/// haqiqiy tovar do'konga kirib kelishi va skaner ikkita tovarga tushib
/// qolishi mumkin edi.</para>
///
/// <para><b>Nega tasodifiy, ketma-ket emas.</b> Ketma-ket raqam uchun mavjud
/// kodlardan maksimumni topish kerak bo'lardi — bu esa satrni kesib songa
/// aylantirishni talab qiladi va parallel yaratishda qulf kerak bo'ladi.
/// Tasodifiy 10 xonali qism 10 milliard variant beradi; bir do'konda bir necha
/// ming tovar borligini hisobga olsak, to'qnashuv ehtimoli amalda nol. Baribir
/// ehtimolga tayanmaymiz: yagonalikni bazadagi unikal indeks kafolatlaydi, bu
/// yerda esa bir necha marta qayta urinib ko'riladi.</para>
/// </summary>
public static class Ean13
{
    /// <summary>Ichki (do'kon) diapazoni: 20…29.</summary>
    private const int InternalPrefixLow = 20;
    private const int InternalPrefixHigh = 29;

    /// <summary>
    /// EAN-13 nazorat raqami (mod-10). O'ngdan chapga: toq o'rinlar 3 ga,
    /// juftlari 1 ga ko'paytiriladi; yig'indini 10 gacha to'ldiruvchi son.
    /// Skaner shu raqam orqali noto'g'ri o'qishni O'ZI aniqlaydi — kod
    /// yarim o'qilsa, u tovarni qo'shmaydi.
    /// </summary>
    public static int CheckDigit(string first12)
    {
        if (first12 is null || first12.Length != 12 || !first12.All(char.IsAsciiDigit))
            throw new ArgumentException("EAN-13 nazorat raqami uchun aynan 12 ta raqam kerak.", nameof(first12));

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = first12[i] - '0';
            // Chapdan sanaganda 0-indeks 1 ga, 1-indeks 3 ga ko'payadi.
            sum += i % 2 == 0 ? digit : digit * 3;
        }
        return (10 - sum % 10) % 10;
    }

    /// <summary>Kod 13 xonali va nazorat raqami to'g'rimi.</summary>
    public static bool IsValid(string? code) =>
        code is { Length: 13 }
        && code.All(char.IsAsciiDigit)
        && CheckDigit(code[..12]) == code[12] - '0';

    /// <summary>
    /// Kiritilgan kodni EAN-13 ga keltiradi yoki nima uchun bo'lmasligini aytadi.
    ///
    /// <para>Zavod yorlig'idagi kod har doim ham EAN-13 emas: AQSh va Kanada
    /// tovarlarida ko'pincha UPC-A — 12 xonali. Standart bo'yicha UPC-A oldiga
    /// bitta «0» qo'yilsa, aynan shu tovarning EAN-13 shakli chiqadi, shuning
    /// uchun uni rad etmasdan o'giramiz.</para>
    ///
    /// <para>Qolgan holatlarda XATO SHU YERDA aytiladi. Ilgari noto'g'ri kod
    /// bemalol saqlanar, xato esa yorliq chop etishda chiqardi — omborchi kodni
    /// kiritganidan ancha keyin va butunlay boshqa ekranda. Sababi ham
    /// ko'rinmasdi: «noto'g'ri parametr» degan umumiy xabar chiqardi.</para>
    /// </summary>
    /// <param name="normalized">Probellari olib tashlangan kod.</param>
    /// <param name="error">Muvaffaqiyatsiz bo'lsa — kassirga ko'rsatiladigan sabab.</param>
    public static bool TryNormalizeToEan13(string normalized, out string? code, out string? error)
    {
        code = null;
        error = null;

        if (!normalized.All(char.IsAsciiDigit))
        {
            error = "Shtrix-kod faqat raqamlardan iborat bo'lishi kerak.";
            return false;
        }

        // UPC-A (12 xona) → EAN-13: oldiga «0».
        var candidate = normalized.Length == 12 ? "0" + normalized : normalized;

        if (candidate.Length != 13)
        {
            error = $"Shtrix-kodda 13 ta raqam bo'lishi kerak (UPC-A uchun 12), hozir — {normalized.Length} ta.";
            return false;
        }

        if (CheckDigit(candidate[..12]) != candidate[12] - '0')
        {
            error = "Shtrix-kodning nazorat raqami mos kelmadi — raqamlarni tekshiring.";
            return false;
        }

        code = candidate;
        return true;
    }

    /// <summary>Kod shu tizim chiqargan ichki koddir (20…29 bilan boshlanadi).</summary>
    public static bool IsInternal(string? code) =>
        IsValid(code)
        && int.TryParse(code![..2], out var prefix)
        && prefix is >= InternalPrefixLow and <= InternalPrefixHigh;

    /// <summary>
    /// Yangi ichki kod: 2 xonali prefiks + 10 tasodifiy raqam + nazorat raqami.
    /// Kriptografik generator ishlatiladi — <c>Random</c> bir vaqtda yaratilgan
    /// ikki chaqiruvda bir xil urug'dan bir xil ketma-ketlik berishi mumkin.
    /// </summary>
    public static string NewInternal()
    {
        var prefix = RandomNumberGenerator.GetInt32(InternalPrefixLow, InternalPrefixHigh + 1);
        Span<char> digits = stackalloc char[13];
        prefix.ToString("D2").CopyTo(digits);
        for (var i = 2; i < 12; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));

        var first12 = new string(digits[..12]);
        digits[12] = (char)('0' + CheckDigit(first12));
        return new string(digits);
    }
}
