using System.Globalization;
using System.Text;
using Buildix.Application.Services.Barcodes;

namespace Buildix.Application.Services.Printing;

/// <summary>
/// Tovar yorlig'ini yorliq printerining O'Z tilida (TSPL) yozadi.
///
/// <para><b>Nega rasm emas.</b> Rasm yo'lida o'lcham DRAYVERDAN keladi:
/// qog'oz o'lchami Windows drayverida oldindan yaratilgan bo'lishi kerak,
/// aks holda drayver so'ralgan o'lchamni o'ziga eng yaqin standartga
/// almashtiradi va maket yorliqqa siljib tushadi. Do'konda rulon o'zgarib
/// turadi (57×38, 57×30, 58×40…), ya'ni har bir rulon uchun drayverda qo'lda
/// qog'oz yaratib chiqishga to'g'ri kelardi. TSPL da o'lcham har bir jobning
/// ICHIDA beriladi (<c>SIZE</c>) — drayverga umuman tegilmaydi va istalgan
/// rulon ishlaydi.</para>
///
/// <para><b>Ikkinchi foydasi — aniqlik.</b> Shtrix kodni printerning o'zi
/// 203 dpi da chizadi. Rasm yo'lida esa server PNG chizadi, drayver uni
/// QAYTA rasterlaydi va chiziq qirralari «yuvilib» ketadi — arzon skaner
/// bunday kodni burchak ostida o'qiy olmasligi mumkin.</para>
///
/// <para>Bu <see cref="EscPosReceipt"/> bilan bir xil yondashuv: server
/// baytlarni yasaydi, qobiq ularni XOM holda printerga uzatadi.</para>
/// </summary>
internal static class TsplLabel
{
    /// <summary>
    /// CP866 .NET Core da sukut bo'yicha YO'Q — u faqat qo'shimcha provayder
    /// bilan ochiladi (izohi <see cref="EscPosReceipt"/> da).
    /// </summary>
    static TsplLabel() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Printer zichligi: 203 dpi = 8 nuqta/mm.
    /// </summary>
    /// <remarks>
    /// Arzon yorliq printerlarining deyarli hammasi (TSC, Gprinter, Xprinter,
    /// Zebra GK/GC) shu zichlikda. 300 dpi modellar ham bor, lekin ularda
    /// TSPL o'lchamni MILLIMETRDA qabul qiladi va nuqta hisobini printerning
    /// o'zi qiladi — bu yerda nuqta faqat joylashuv uchun kerak, shuning
    /// uchun 300 dpi da ham maket to'g'ri chiqadi, atigi biroz mayda
    /// bo'ladi.
    /// </remarks>
    private const double Dpmm = 8.0;

    /// <summary>Chetdan qoldiriladigan bo'sh joy, mm.</summary>
    private const double MarginMm = 1.5;

    /// <summary>
    /// Shtrix kod modulining eng kichik eni, nuqtada. 2 nuqta = 0.25 mm.
    /// </summary>
    /// <remarks>
    /// EAN-13 standarti eng ingichka modul uchun 0.264 mm ni ko'rsatadi;
    /// 203 dpi da bu 2.1 nuqta. Bir nuqtalik modul (0.125 mm) ko'zga
    /// to'g'ri ko'rinadi, lekin arzon qo'l skaneri uni burchak ostida yoki
    /// biroz kir yorliqda o'qiy olmaydi — qurilish do'konida ikkalasi ham
    /// odatiy hol.
    /// </remarks>
    private const int MinNarrowDots = 2;

    // JIM ZONA ni bu yerda QO'SHMAYMIZ.
    //
    // `BarcodeSvg.ModuleCount` ZXing dan keladi va u jim zonani ALLAQACHON
    // o'z ichiga oladi: EAN-13 uchun 113 = 95 chiziq moduli + ikki chetda
    // 9 tadan. Ustiga yana qo'shilsa, kod keraksiz torayadi va — muhimi —
    // markazlash hisobiga kiradi, ya'ni kod yorliqning chap tomoniga
    // suriladi. Haqiqiy printerda tekshirilgan to'g'ri joylashuv
    // 57 mm yorliqda x=58 nuqta; ikki marta hisoblanganda u x=31 ga
    // tushib ketgan edi.

    /// <summary>
    /// TSPL ichki shriftlari: nomi va bitta belgining eni/bo'yi (nuqtada).
    /// Katta yorliqda kattaroq shrift tanlanadi.
    /// </summary>
    private static readonly (string Name, int W, int H)[] Fonts =
    [
        ("1", 8, 12),
        ("2", 12, 20),
        ("3", 16, 24),
    ];

    /// <summary>
    /// Yorliqlar to'plamini bitta TSPL jobiga yig'adi.
    /// </summary>
    /// <param name="labels">Nom + kod + artikul + nusxa soni.</param>
    /// <param name="widthMm">Rulon eni.</param>
    /// <param name="heightMm">Yorliq bo'yi.</param>
    /// <param name="gapMm">Yorliqlar orasidagi tirqish.</param>
    /// <param name="offsetMm">
    /// Vertikal siljish tuzatmasi. FAQAT musbat: TSPL da <c>SHIFT</c>
    /// diapazoni 0…1016 va manfiy son firmware tomonidan ishorasiz deb
    /// o'qilib, maketni bir necha santimetrga sakratib yuboradi — bitta
    /// yorliq o'rniga ikkitasiga bo'linib tushadi (haqiqiy printerda
    /// ko'rilgan).
    ///
    /// <para>Nol bo'lsa ham buyruq YUBORILADI. <c>SHIFT</c> — printerda
    /// SAQLANADIGAN sozlama: u keyingi buyruqqacha yoki quvvat o'chgunicha
    /// turadi. Yubormaslik «siljish yo'q» degani emas, «oldingi jobdan
    /// qolganini saqlab qol» degani. Aynan shu sababdan bu sarlavhada
    /// <c>SIZE</c>, <c>GAP</c>, <c>DIRECTION</c> va qolganlari ham har safar
    /// qayta e'lon qilinadi — <c>SHIFT</c> ular orasida yagona istisno
    /// bo'lib qolgan edi. Oqibati: 57×30 rulonga o'tib siljishni nolga
    /// qo'ygan do'konda yorliqlar baribir surilgan chiqaverardi va buni
    /// interfeysdan tuzatib bo'lmasdi (manfiy qiymat taqiqlangan, nol esa
    /// hech narsa qilmasdi) — faqat printerni o'chirib yoqish yordam
    /// berardi.</para>
    /// </param>
    internal static byte[] Build(
        IReadOnlyList<LabelData> labels,
        double widthMm,
        double heightMm,
        double gapMm = 2,
        double offsetMm = 0)
    {
        var body = new List<byte>(2048);
        // TSPL buyruqlari ASCII; matn esa CP866 orqali ketadi — kirillcha
        // nomli tovar ham bosilsin.
        var enc = Encoding.GetEncoding(866);

        void Line(string text) => body.AddRange(enc.GetBytes(text + "\r\n"));

        var w = Dots(widthMm);
        var h = Dots(heightMm);

        // ── Job sarlavhasi ──────────────────────────────────────────────
        // SIZE shu yerda berilgani uchun drayverdagi qog'oz o'lchami umuman
        // ahamiyatsiz bo'ladi — aynan shu butun o'zgarishning maqsadi.
        Line($"SIZE {Mm(widthMm)} mm,{Mm(heightMm)} mm");
        Line($"GAP {Mm(gapMm)} mm,0 mm");
        // 1 — maket rulon chiqish yo'nalishiga mos; 0 bo'lsa u 180° teskari
        // chiqadi va yorliq qo'lda o'qilmaydi.
        Line("DIRECTION 1");
        Line("REFERENCE 0,0");
        Line("DENSITY 8");
        Line("SPEED 3");
        // Kirill kod sahifasi. Lotin belgilar 0–127 oralig'ida va undan
        // ta'sirlanmaydi, ya'ni o'zbekcha nom ham, ruscha nom ham chiqadi.
        Line("CODEPAGE 866");
        Line($"SHIFT {Dots(Math.Max(0, offsetMm))}");

        foreach (var label in labels)
        {
            Line("CLS");
            Compose(Line, label, w, h, heightMm);
            // Nusxa TSPL ning O'Z sanog'i bilan: `PRINT 1,5` beshta yorliq
            // chiqaradi. Rasm yo'lida buning uchun besh marta sahifa
            // takrorlanardi va besh barobar ma'lumot uzatilardi.
            Line($"PRINT 1,{Math.Max(1, label.Copies)}");
        }

        return [.. body];
    }

    /// <summary>
    /// Tirqish sensorini kalibrovkalaydi — printer yorliq UZUNLIGINI o'zi
    /// o'lchab xotirasiga yozadi. Bir-ikki yorliq bo'sh chiqadi.
    /// </summary>
    /// <remarks>
    /// <para><b>Rulon almashtirilganda BIRINCHI shu bajariladi.</b> Busiz
    /// printer oldingi rulonning uzunligini ishlatadi va maket yorliqqa
    /// emas, tirqishga tushadi. Haqiqiy printerda tekshirilgan: kalibrovkasiz
    /// 57×38 rulonda maket xato o'lchamda chiqdi, <c>GAPDETECT</c> dan keyin
    /// esa aynan joyiga tushdi.</para>
    ///
    /// <para>Bu ATAYLAB alohida amal, chop etishning ichiga qo'shilmagan:
    /// har bosishda kalibrovka qilish har safar ikkita yorliqni bekorga
    /// sarflardi.</para>
    /// </remarks>
    internal static byte[] Calibrate(double widthMm, double heightMm, double gapMm = 2)
    {
        var body = new StringBuilder();
        body.Append($"SIZE {Mm(widthMm)} mm,{Mm(heightMm)} mm\r\n");
        body.Append($"GAP {Mm(gapMm)} mm,0 mm\r\n");
        body.Append("DIRECTION 1\r\n");
        // Kalibrovka XOM holatda o'lchashi kerak: oldingi jobdan qolgan
        // siljish sensor natijasiga aralashmasin.
        body.Append("SHIFT 0\r\n");
        body.Append("CLS\r\n");
        body.Append("GAPDETECT\r\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>
    /// Sinov yorlig'i — chetdan 1 mm ichkarida ramka va markaziy xoch.
    /// </summary>
    /// <remarks>
    /// <para>Joylashuvni AYNAN shu bilan tekshiriladi: ramka yorliqqa teng
    /// tushsa o'lcham va siljish to'g'ri. Siljigan bo'lsa, farqni lineyka
    /// bilan o'lchab «Vertikal siljish» sozlamasiga kiritish mumkin —
    /// taxmin qilish kerak emas.</para>
    ///
    /// <para>Ramka namunaviy tovar yorlig'idan ko'ra foydaliroq: bir necha
    /// millimetrlik siljishni matndan payqab bo'lmaydi, chetdan teng
    /// masofadagi to'rtburchakdan esa darhol ko'rinadi.</para>
    /// </remarks>
    internal static byte[] TestLabel(double widthMm, double heightMm, double gapMm = 2, double offsetMm = 0)
    {
        var w = Dots(widthMm);
        var h = Dots(heightMm);
        var edge = Dots(1);

        var body = new StringBuilder();
        body.Append($"SIZE {Mm(widthMm)} mm,{Mm(heightMm)} mm\r\n");
        body.Append($"GAP {Mm(gapMm)} mm,0 mm\r\n");
        body.Append("DIRECTION 1\r\nREFERENCE 0,0\r\nDENSITY 8\r\nSPEED 3\r\n");
        // Nol bo'lsa ham yuboriladi — sabab `Build` izohida. Sinov yorlig'ida
        // bu ayniqsa muhim: u NOSOZLIKNI aniqlash uchun bosiladi va oldingi
        // jobdan qolgan siljishni ko'rsatib qo'ysa, texnik mavjud bo'lmagan
        // muammoni tuzatib o'tirardi.
        body.Append($"SHIFT {Dots(Math.Max(0, offsetMm))}\r\n");
        body.Append("CLS\r\n");
        // Ramka — yorliq chetidan 1 mm ichkarida.
        body.Append($"BOX {edge},{edge},{w - edge},{h - edge},3\r\n");
        // Markaziy xoch — markaz qayerga tushganini ko'rsatadi.
        body.Append($"BAR {w / 2},{edge},2,{h - 2 * edge}\r\n");
        body.Append($"BAR {edge},{h / 2},{w - 2 * edge},2\r\n");
        body.Append($"TEXT {w / 2},{h / 2 - 12},\"2\",0,1,1,2,\"{Mm(widthMm)}x{Mm(heightMm)} mm\"\r\n");
        body.Append("PRINT 1,1\r\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>
    /// Bitta yorliqning maketi: nom (kerak bo'lsa ikki qatorda), shtrix kod,
    /// artikul.
    /// </summary>
    private static void Compose(Action<string> line, LabelData label, int w, int h, double heightMm)
    {
        var margin = Dots(MarginMm);
        var printable = w - 2 * margin;
        var centre = w / 2;

        // ── Nom ─────────────────────────────────────────────────────────
        // Shrift yorliq eniga qarab tanlanadi: kichik rulonda katta shrift
        // ikki-uch so'zdan keyin chetdan chiqib ketardi.
        var font = PickFont(printable);
        var perLine = Math.Max(1, printable / font.W);
        var nameLines = Wrap(Clean(label.ProductName), perLine).Take(2).ToList();
        var nameHeight = nameLines.Count * (font.H + 2);

        // ── Shtrix kod ──────────────────────────────────────────────────
        // Modul eni butun NUQTA bo'lishi kerak: kasr berilsa printer uni
        // yaxlitlaydi va chiziqlar notekis chiqadi — skaner uchun eng yomon
        // holat.
        var modules = BarcodeSvg.ModuleCount(label.Barcode);

        // Modul eni kamida 2 nuqta (0.25 mm) bo'lsin. Butun songa bo'lishda
        // kichik rulonda u 1 nuqtaga (0.125 mm) tushib ketardi — EAN-13
        // standarti ruxsat beradigan eng ingichka moduldan ikki barobar
        // ingichka. Bunday kod ko'zga to'g'ri ko'rinadi, lekin arzon qo'l
        // skaneri uni burchak ostida yoki biroz kir yorliqda o'qiy olmaydi.
        //
        // Ikki nuqta chekka bo'sh joyga sig'masa, o'sha bo'sh joyni ham
        // ishlatamiz: kodning o'qilishi 1.5 mm hoshiyadan muhimroq.
        var narrow = printable / modules;
        if (narrow < MinNarrowDots && modules * MinNarrowDots <= w) narrow = MinNarrowDots;
        narrow = Math.Max(1, narrow);

        var codeX = Math.Max(0, (w - modules * narrow) / 2);

        // Raqamlar (HRI) chiziqlar ostiga PRINTER tomonidan chiziladi —
        // ular uchun joy ajratiladi, lekin buyruq yozilmaydi.
        var hriHeight = Fonts[0].H + 4;
        var sku = Clean(label.Sku);
        var skuHeight = sku.Length > 0 ? Fonts[0].H : 0;

        // Chiziqlar balandligi — qolgan bo'sh joyga qarab, lekin yorliq
        // bo'yining 45% idan oshmasin (aks holda uzun yorliqda kod
        // cho'zilib, nomga joy qolmasdi) va 6 mm dan past bo'lmasin
        // (pastroq bo'lsa qo'l skaneri burchak ostida o'qiy olmaydi).
        var free = h - 2 * margin - nameHeight - hriHeight - skuHeight;
        var bars = Math.Clamp(free, Dots(6), Math.Max(Dots(6), (int)(h * 0.45)));

        // ── Vertikal markazlash ─────────────────────────────────────────
        // Maket yuqoridan boshlanganda pastda katta bo'sh joy qolardi
        // (57×38 da 13 mm ga yaqin) va yorliq qiyshiq ko'rinardi. PDF
        // chizuvchisi ham aynan shunday markazlaydi — ikkala yo'l bir xil
        // ko'rinishda qolishi kerak.
        var total = nameHeight + bars + hriHeight + skuHeight;
        var y = Math.Max(margin, (h - total) / 2);

        foreach (var part in nameLines)
        {
            // Hizalash parametri (2 = markaz) TSPL2 da x ni MARKAZ deb oladi.
            line($"TEXT {centre},{y},\"{font.Name}\",0,1,1,2,\"{Escape(part)}\"");
            y += font.H + 2;
        }

        var kind = Symbology.KindOf(label.Barcode) == BarcodeKind.Ean13 ? "EAN13" : "128";
        // HRI = 2 → raqamlar chiziqlar ostida, markazda. Ular printer
        // tomonidan chiziladi, ya'ni kod bilan har doim mos keladi.
        line($"BARCODE {codeX},{y},\"{kind}\",{bars},2,0,{narrow},{narrow},\"{Escape(label.Barcode)}\"");
        y += bars + hriHeight;

        // ── Artikul ─────────────────────────────────────────────────────
        // Bo'sh bo'lsa qator umuman chiqmaydi: yorliq kichik va bo'sh
        // «Art.» yozuvi faqat joy egallardi.
        if (skuHeight > 0 && y + skuHeight <= h)
        {
            // Artikul KESILADI. Nom kabi ikki qatorga bo'linmaydi — unga joy
            // yo'q — lekin kesilmasa uzun artikul yorliqning ikkala chetidan
            // ham chiqib ketardi: matn markazdan chizilgani uchun u chapga
            // ham, o'ngga ham oshib ketadi.
            var fits = Math.Max(1, printable / Fonts[0].W);
            var shown = sku.Length > fits ? sku[..fits] : sku;
            line($"TEXT {centre},{y},\"1\",0,1,1,2,\"{Escape(shown)}\"");
        }
    }

    /// <summary>
    /// Yorliq eniga mos ichki shrift — eng kattasidan boshlab.
    /// </summary>
    /// <remarks>
    /// Chegara: nom uchun bitta qatorga kamida 16 belgi sig'ishi kerak.
    /// Undan kam bo'lsa hatto qisqa nom ham ikki qatorga bo'linib ketardi va
    /// shtrix kodga joy qolmasdi.
    /// </remarks>
    private static (string Name, int W, int H) PickFont(int printableDots)
    {
        for (var i = Fonts.Length - 1; i > 0; i--)
            if (printableDots / Fonts[i].W >= 16) return Fonts[i];
        return Fonts[0];
    }

    /// <summary>
    /// Matnni qatorga sig'adigan bo'laklarga bo'ladi — TSPL ning
    /// <c>TEXT</c> buyrug'i satrni O'ZI ko'chirmaydi, uzun nom shunchaki
    /// yorliqdan chiqib ketadi.
    /// </summary>
    /// <remarks>
    /// So'z chegarasi bo'yicha: nomni o'rtasidan kesish uni o'qib bo'lmas
    /// qiladi. Bitta so'z qatordan uzun bo'lsa — majburan kesiladi, aks
    /// holda halqa cheksiz bo'lardi. <see cref="EscPosReceipt"/> dagi
    /// bilan bir xil qoida.
    /// </remarks>
    private static IEnumerable<string> Wrap(string text, int cols)
    {
        if (text.Length == 0) yield break;

        var line = new StringBuilder(cols);
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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

    /// <summary>
    /// Printer tushunmaydigan belgilarni almashtiradi — o'zbekcha matndagi
    /// tipografik apostrof va uzun tire CP866 da yo'q
    /// (<see cref="EscPosReceipt"/> dagi bilan bir xil jadval).
    /// </summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('ʻ', '\'')
            .Replace('ʼ', '\'')
            .Replace("—", "-")
            .Replace("–", "-")
            // Uzilmas probel — ATAYLAB ` ` shaklida. Ilgari bu yerda
            // belgining o'zi turardi va nusxa ko'chirishda u oddiy probelga
            // aylanib qolgan: qator «probelni probelga almashtirish» degan
            // ma'nosiz amalga aylangan. Oqibati ko'rinmas edi — nom bitta
            // uzilmas so'z bo'lib qolar va satrga ko'chirilmasdan yorliqdan
            // chiqib ketardi.
            .Replace(' ', ' ')
            .Replace("\r", string.Empty)
            .Replace("\n", " ")
            .Trim();
    }

    /// <summary>
    /// TSPL satr chegarasi — qo'shtirnoq va teskari chiziq.
    /// </summary>
    /// <remarks>
    /// Qochirilmasa qo'shtirnoqli nom («Sement "Bekobod"») buyruqni erta
    /// tugatar va printer qolganini alohida buyruq deb o'qib, yorliqni
    /// buzardi.
    /// </remarks>
    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Millimetrni nuqtaga.</summary>
    private static int Dots(double mm) => (int)Math.Round(mm * Dpmm);

    /// <summary>
    /// TSPL uchun son. O'nlik ajratgich — HAR DOIM nuqta.
    /// </summary>
    /// <remarks>
    /// Do'kon Windows'i rus tilida va u yerda <c>ToString</c> sukut bo'yicha
    /// VERGUL qo'yadi. «SIZE 57,5 mm» buyrug'ini printer tushunmaydi va uni
    /// jimgina tashlab yuboradi — yorliq esa oldingi o'lchamda chiqaveradi.
    /// </remarks>
    private static string Mm(double v) =>
        v.ToString("0.##", CultureInfo.InvariantCulture);
}
