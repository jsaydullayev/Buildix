using System.Text;
using Buildix.Application.Services.Reports;

namespace Buildix.Application.Services.Printing;

/// <summary>
/// Kassa chekini termal printerning O'Z tilida (ESC/POS) yozadi.
///
/// <para><b>Nega rasm emas.</b> Rasm yo'li ishlaydi, lekin sekin: server
/// chekni chizadi va rasterlaydi (~1–2 s), qobiq uni ko'rinmas oynada
/// ochadi, so'ng Windows drayveri katta bitmapni QAYTA rasterlab
/// printerga yuboradi. Kassir tugmani bosgach bir necha soniya kutadi —
/// navbat turgan do'konda bu sezilarli.</para>
///
/// <para>ESC/POS da chek bir necha kilobayt matn va buyruqdan iborat.
/// Chizishni printerning o'zi bajaradi, ya'ni qog'oz deyarli darhol
/// chiqadi va oxirida QIRQILADI — buni rasm yo'li umuman qila
/// olmaydi.</para>
///
/// <para><b>Ustunlar.</b> Termal printerda shrift qat'iy kenglikda:
/// 58 mm rulonga 32 belgi, 80 mm ga 48 belgi sig'adi (Font A). Nom chapga,
/// summa o'ngga tekislanadi va oradagi joy bo'shliq bilan to'ldiriladi —
/// aks holda ular bir-biriga yopishib qolardi. Hisob AYNAN Font A va nol
/// belgi oralig'iga tayanadi, shuning uchun ikkalasi ham chek boshida
/// ochiq o'rnatiladi.</para>
///
/// <para><b>O'qilishi.</b> Butun chek qalin (bold) bosiladi: termal bosh
/// arzon qog'ozga ingichka va och yozadi, do'kon yorug'ida esa bunday
/// chekni o'qib bo'lmaydi. Do'kon nomi va JAMI ikki baravar kattalikda —
/// ular qatorga ikki barobar kam belgi sig'adigan qilib hisoblanadi.</para>
/// </summary>
internal static class EscPosReceipt
{
    /// <summary>
    /// CP866 .NET Core da sukut bo'yicha YO'Q — u faqat qo'shimcha
    /// provayder bilan ochiladi. Ro'yxatdan o'tkazish shu yerda: aks holda
    /// u faqat API ishga tushishida qilinar va sinovlar «kodlash topilmadi»
    /// bilan yiqilardi. Takroriy chaqiruv zararsiz.
    /// </summary>
    static EscPosReceipt() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ── Buyruqlar ───────────────────────────────────────────────────────
    private static readonly byte[] Init = [0x1B, 0x40];              // ESC @
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];   // ESC a 0
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01]; // ESC a 1
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];      // ESC E 1
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];     // ESC E 0
    private static readonly byte[] DoubleOn = [0x1D, 0x21, 0x11];    // GS ! — ikki baravar
    private static readonly byte[] DoubleOff = [0x1D, 0x21, 0x00];

    /// <summary>
    /// Font A (12×24 nuqta). <see cref="Columns"/> hisobi AYNAN shunga
    /// tayanadi: Font B ensizroq (9 nuqta) va unda qatorga boshqacha
    /// miqdorda belgi sig'adi — ustunlar siljib ketardi.
    /// </summary>
    private static readonly byte[] FontA = [0x1B, 0x4D, 0x00];       // ESC M 0

    /// <summary>
    /// Belgilar orasidagi QO'SHIMCHA joy — nol.
    /// </summary>
    /// <remarks>
    /// Qo'shimcha joy qatorga sig'adigan belgilar sonini kamaytiradi va
    /// ustunlar hisobini buzadi: 48 belgilik qator printerda ikkiga
    /// bo'linib, summa keyingi qatorga tushib ketardi. Buni ochiq nolga
    /// qo'yamiz — oldingi ish qoldirgan sozlama chekka o'tmasin.
    /// </remarks>
    private static readonly byte[] NoCharSpacing = [0x1B, 0x20, 0x00]; // ESC SP 0

    /// <summary>
    /// Kod sahifasi 17 = CP866 (kirill). Lotin belgilar 0–127 oralig'ida va
    /// undan ta'sirlanmaydi, ya'ni o'zbekcha ham, ruscha ham chiqadi.
    /// </summary>
    private static readonly byte[] CodePage866 = [0x1B, 0x74, 0x11];

    /// <summary>Qog'ozni to'liq qirqish (GS V 65 n) — oldidan 4 qator suriladi.</summary>
    private static readonly byte[] Cut = [0x1D, 0x56, 0x41, 0x04];

    /// <summary>Bir qatordagi belgilar soni.</summary>
    private static int Columns(int widthMm) => widthMm <= 58 ? 32 : 48;

    internal static byte[] Build(ReportPdfRenderer.InvoiceData data, string lang, int widthMm)
    {
        var ru = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string rus) => ru ? rus : uz;

        var cols = Columns(widthMm);
        var enc = Encoding.GetEncoding(866);
        var body = new List<byte>(2048);

        void Raw(byte[] cmd) => body.AddRange(cmd);
        void Line(string text = "") => body.AddRange(enc.GetBytes(Clean(text) + "\n"));

        Raw(Init);
        Raw(FontA);
        Raw(NoCharSpacing);
        Raw(CodePage866);

        // Butun chek QALIN bosiladi. Termal bosh arzon qog'ozga ingichka,
        // och kulrang chiziq bilan yozadi — do'kon yorug'ida bunday chekni
        // o'qib bo'lmasdi. Qalin rejimda printer har nuqtani ikki marta
        // uradi va harflar to'q chiqadi.
        Raw(BoldOn);

        // ── Do'kon nomi ─────────────────────────────────────────────────
        // Ikki baravar kattalikda, ya'ni qatorga ikki barobar KAM belgi
        // sig'adi — uzun nom shunga qarab bo'linadi. Aks holda printer uni
        // o'zicha kesar va chek nomsiz boshlanardi.
        Raw(AlignCenter);
        Raw(DoubleOn);
        foreach (var part in Wrap(data.MarketName, cols / 2)) Line(part);
        Raw(DoubleOff);
        if (!string.IsNullOrWhiteSpace(data.MarketDescription))
            Line(data.MarketDescription);

        // ── Do'kon rekvizitlari ─────────────────────────────────────────
        // Manzil va telefon — sozlamalardan. Mijoz chekni saqlab qo'yadi va
        // qaytarish yoki kafolat uchun do'konni AYNAN shundan topadi.
        // To'ldirilmagan maydon qator ham egallamaydi: rulon tor va bo'sh
        // qatorlar chekni cho'zardi.
        foreach (var part in Wrap(data.MarketAddress, cols)) Line(part);
        if (!string.IsNullOrWhiteSpace(data.MarketPhone))
            Line(L("Tel: ", "Тел: ") + Clean(data.MarketPhone));

        // Egasi yozgan matn («Chek tepasidagi matn») — aksiya, ish vaqti,
        // istalgan yozuv.
        foreach (var part in Wrap(data.ReceiptHeader, cols)) Line(part);

        // Uzun chiziq: do'kon nomini chekning qolgan qismidan ajratadi.
        Raw(AlignLeft);
        Line(new string('=', cols));

        // ── Rekvizitlar ─────────────────────────────────────────────────
        // Chek raqami — QAYTARISH shu raqam bo'yicha topiladi. Ilgari bu
        // yerda sotuv identifikatorining qisqartmasi («#9BBB18») turardi:
        // u hech qanday qidiruvga tushmasdi va kassir qo'lida chek bilan
        // sotuvni topa olmasdi.
        Line(Pair(L("Chek", "Чек"), $"№{data.SaleNumber}", cols));
        Line(Pair(L("Sana", "Дата"), data.Date.ToString("dd.MM.yyyy HH:mm"), cols));
        // Ism SIG'DIRILADI, qirqilmaydi. `Pair` uzun o'ng qismni qatorga
        // sig'dirish uchun uni BOSHIDAN kesardi — «Abdurahmonov Jasurbek»
        // 32 belgilik rulonda «rahmonov Jasurbek» bo'lib chiqar va chekda
        // kim sotganini aniqlab bo'lmasdi. Sonlar uchun Pair to'g'ri
        // (summa hech qachon uzun emas), ismlar uchun esa yo'q.
        foreach (var part in Wrap(L("Sotuvchi: ", "Продавец: ") + data.SellerName, cols))
            Line(part);
        // Mijoz YO'Q bo'lsa qator umuman bosilmaydi. Ma'lumot to'plami bu
        // holatda «Mijoz ko'rsatilmagan» degan o'rinbosar matn beradi va u
        // chekka chiqsa foydasiz qator bo'lardi — rulon esa tor.
        if (HasCustomer(data.CustomerName))
            foreach (var part in Wrap(L("Mijoz: ", "Клиент: ") + data.CustomerName, cols))
                Line(part);

        // ── To'lov ──────────────────────────────────────────────────────
        // Aralash to'lovda har bir tur O'Z SUMMASI bilan alohida qatorda
        // chiqadi. Bitta qatorga sig'dirilsa («Naqd 500 000, Karta 402
        // 000») u 32 belgilik rulonda kesilib qolardi, mijoz esa qaysi
        // puldan qancha ketganini chekdan bila olmasdi.
        var payments = data.Payments ?? [];
        if (payments.Count > 1)
        {
            Line(L("To'lov", "Оплата") + ":");
            foreach (var p in payments)
                Line(Pair("  " + p.Label, Money(p.Amount), cols));
        }
        else
        {
            Line(Pair(L("To'lov", "Оплата"), data.PaymentType, cols));
        }

        Line(new string('-', cols));

        // ── Tovarlar ────────────────────────────────────────────────────
        // Nom ALOHIDA qatorda: 32 belgili rulonda nom va summa bir qatorga
        // sig'masdi va nom qirqilardi.
        foreach (var item in data.Items)
        {
            // Nom QATORGA SIG'DIRILADI. Sig'masa printer uni o'zicha bo'lardi
            // va uzun nomli tovar chekni o'qib bo'lmas holga keltirardi.
            foreach (var part in Wrap(item.ProductName, cols)) Line(part);
            Line(Pair($"  {Qty(item.Quantity)} x {Money(item.Price)}", Money(item.Total), cols));
        }

        Line(new string('-', cols));

        // ── Yakun ───────────────────────────────────────────────────────
        if (data.DiscountAmount > 0)
        {
            Line(Pair(L("Chegirmasiz", "Без скидки"), Money(data.SubtotalAmount), cols));
            Line(Pair(L("Chegirma", "Скидка"), "-" + Money(data.DiscountAmount), cols));
        }

        // JAMI — chekdagi eng muhim son va uni mijoz bir qarashda ko'rishi
        // kerak. Ikki baravar kattalikda bosiladi, ya'ni qatorga ikki
        // barobar KAM belgi sig'adi: ustunlar shunga qarab hisoblanmasa
        // summa qatordan chiqib ketardi.
        Raw(DoubleOn);
        Line(Pair(L("JAMI", "ИТОГО"), Money(data.TotalAmount), cols / 2));
        Raw(DoubleOff);

        Line(Pair(L("To'landi", "Оплачено"), Money(data.PaidAmount), cols));
        if (data.RemainingAmount > 0)
            Line(Pair(L("Qarz", "Долг"), Money(data.RemainingAmount), cols));

        Line(new string('=', cols));

        Raw(AlignCenter);
        Line(L("Xaridingiz uchun rahmat!", "Спасибо за покупку!"));
        // Egasi yozgan matn («Chek pastidagi matn»).
        foreach (var part in Wrap(data.ReceiptFooter, cols)) Line(part);
        Raw(AlignLeft);

        Raw(BoldOff);
        Raw(Cut);
        return [.. body];
    }

    /// <summary>Haqiqiy mijoz bormi (o'rinbosar matn emasmi).</summary>
    private static bool HasCustomer(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return !n.Equals("Mijoz ko'rsatilmagan", StringComparison.OrdinalIgnoreCase)
            && !n.Equals("Mijoz ko’rsatilmagan", StringComparison.OrdinalIgnoreCase)
            && !n.Equals("Без клиента", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Matnni qator eniga sig'adigan bo'laklarga bo'ladi.
    /// </summary>
    /// <remarks>
    /// So'z chegarasi bo'yicha: nomni o'rtasidan kesish uni o'qib bo'lmas
    /// qiladi. Bitta so'z qatordan uzun bo'lsa (masalan uzun artikul) —
    /// majburan kesiladi, aks holda halqa cheksiz bo'lardi.
    /// </remarks>
    private static IEnumerable<string> Wrap(string? text, int cols)
    {
        var clean = Clean(text);
        if (clean.Length == 0) yield break;

        var line = new StringBuilder(cols);
        foreach (var word in clean.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            while (w.Length > cols)
            {
                if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                yield return w[..cols];
                w = w[cols..];
            }

            if (line.Length == 0) line.Append(w);
            else if (line.Length + 1 + w.Length <= cols) line.Append(' ').Append(w);
            else { yield return line.ToString(); line.Clear(); line.Append(w); }
        }

        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>Chap va o'ng qismni bir qatorga tekislaydi.</summary>
    /// <remarks>
    /// Oradagi joy bo'shliq bilan to'ldiriladi. Busiz nom va summa
    /// bir-biriga YOPISHIB chiqardi («1 x 380 000380 000») va chekni o'qib
    /// bo'lmasdi.
    /// </remarks>
    private static string Pair(string left, string right, int cols)
    {
        left = Clean(left);
        right = Clean(right);

        // Ikkalasi sig'masa chap qism qisqartiriladi: summa HECH QACHON
        // qirqilmasligi kerak — chekdagi eng muhim son o'sha.
        var room = cols - right.Length - 1;
        if (room < 1) return right.Length >= cols ? right[^cols..] : right;
        if (left.Length > room) left = left[..room];

        return left + new string(' ', cols - left.Length - right.Length) + right;
    }

    /// <summary>
    /// Termal printer tushunmaydigan belgilarni almashtiradi.
    /// </summary>
    /// <remarks>
    /// O'zbekcha matnda tipografik apostrof (’) va uzun tire (—) uchraydi.
    /// Ular CP866 da yo'q va printer ularning o'rniga tasodifiy belgi
    /// bosardi — «do’kon» o'rniga «doPkon» kabi.
    /// </remarks>
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace('’', '\'')   // ’
            .Replace('‘', '\'')   // ‘
            .Replace('ʻ', '\'')   // ʻ (o'zbek okina)
            .Replace('ʼ', '\'')   // ʼ
            .Replace("—", "-")    // —
            .Replace("–", "-")    // –
            .Replace(" ", " ")    // uzilmas bo'shliq
            .Replace("\r", string.Empty)
            .Replace("\n", " ");
    }

    /// <summary>Summani bo'shliq bilan ajratib yozadi: 380000 → «380 000».</summary>
    private static string Money(decimal v) =>
        v.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>Miqdorni yozadi: 1000 → «1 000», 2.5 → «2.5».</summary>
    /// <remarks>
    /// Minglik ajratgichi summanikidek BO'SHLIQ. Ilgari bu yerda vergul
    /// qolardi va bitta qatorda ikki xil format chiqardi:
    /// «1,000 x 8 500». Chekdagi sonlar bir xil o'qilishi kerak.
    /// </remarks>
    private static string Qty(decimal v) =>
        v == decimal.Truncate(v)
            ? Money(v)
            : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
