namespace Buildix.Application.Services.Barcodes;

/// <summary>Yorliqda qaysi shtrix-kod turi bosiladi.</summary>
public enum BarcodeKind
{
    /// <summary>Zavod kodi: 13 xonali, nazorat raqami joyida.</summary>
    Ean13,

    /// <summary>
    /// Do'konning o'z kodi — ixtiyoriy uzunlik va belgilar («1», «15», «A-3»).
    /// </summary>
    Code128,
}

/// <summary>
/// Kod qaysi turga tegishli ekanini hal qiladi va uni saqlashga tayyorlaydi.
///
/// <para><b>Nega ikki tur.</b> Do'kondagi tovarlarning bir qismida zavod
/// yorlig'i bor — u EAN-13 (yoki UPC-A) va uni o'zgartirmasdan ishlatish
/// kerak: mijoz boshqa do'konda ham xuddi shu kodni ko'radi. Qolganlarida esa
/// hech qanday kod yo'q va do'kon o'zi raqam beradi — ko'pincha eng oddiysi:
/// «1», «2», «3». Bunday kodni EAN-13 ga majburan sig'dirib bo'lmaydi: u
/// aynan 13 xona va nazorat raqamini talab qiladi.</para>
///
/// <para><b>Nega Code 128.</b> U ixtiyoriy ASCII satrni AYNAN kodlaydi, ya'ni
/// «1» yorlig'i skanerlanganda «1» qaytadi. Muqobil yo'l — «1» ni ichki
/// EAN-13 ga aylantirib saqlash edi, lekin unda omborchi kiritgan raqam bilan
/// bazadagi kod boshqa-boshqa bo'lib qolardi va u buni tushunmasdi.</para>
///
/// <para>Har ikki tur ham qo'l skanerlarida sukut bo'yicha yoqilgan, shuning
/// uchun kassada qo'shimcha sozlash talab qilinmaydi.</para>
/// </summary>
public static class Symbology
{
    /// <summary>Code 128 ga sig'adigan eng uzun kod (amaliy chegara).</summary>
    public const int MaxCode128Length = 48;

    /// <summary>Kod qaysi tur bilan bosilishini aytadi.</summary>
    public static BarcodeKind KindOf(string code) =>
        Ean13.IsValid(code) ? BarcodeKind.Ean13 : BarcodeKind.Code128;

    /// <summary>
    /// Kiritilgan kodni saqlashga tayyorlaydi.
    ///
    /// <para>Tartib muhim: avval zavod kodi bo'lish ehtimoli tekshiriladi
    /// (12/13 xonali raqam), chunki u standart va uni EAN-13 sifatida bosish
    /// kerak. Faqat shundan keyin ichki kod deb qabul qilinadi — aks holda
    /// «4780123456789» kabi nazorat raqami xato kod jimgina ichki kod bo'lib
    /// saqlanar va omborchi zavod yorlig'ini noto'g'ri kiritganini bilmasdi.</para>
    /// </summary>
    /// <param name="raw">Foydalanuvchi kiritgani (probellari olib tashlangan).</param>
    /// <param name="code">Saqlanadigan kod.</param>
    /// <param name="error">Qabul qilinmasa — sabab.</param>
    public static bool TryNormalize(string raw, out string? code, out string? error)
    {
        code = null;
        error = null;

        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            error = "Shtrix-kod bo'sh bo'lmasin.";
            return false;
        }

        // ── Zavod kodi bo'lishi mumkinmi ──────────────────────────────────
        // Aynan 12 yoki 13 xonali raqam — bu EAN-13/UPC-A da'vosi. Bunday
        // holatda nazorat raqami MAJBURIY tekshiriladi: xato kodni ichki kod
        // deb qabul qilish omborchini adashtirardi.
        if (trimmed.All(char.IsAsciiDigit) && trimmed.Length is 12 or 13)
            return Ean13.TryNormalizeToEan13(trimmed, out code, out error);

        // ── Do'konning o'z kodi ───────────────────────────────────────────
        if (trimmed.Length > MaxCode128Length)
        {
            error = $"Shtrix-kod {MaxCode128Length} ta belgidan uzun bo'lmasin.";
            return false;
        }

        // Code 128 faqat ASCII (0-127) ni kodlaydi. Kirill yoki emoji kiritilsa
        // yorliq bosishda portlardi — shuning uchun shu yerda aytamiz.
        if (!trimmed.All(ch => ch is >= ' ' and <= '~'))
        {
            error = "Shtrix-kodda faqat lotin harflari, raqamlar va oddiy belgilar ishlatilsin.";
            return false;
        }

        code = trimmed;
        return true;
    }
}
