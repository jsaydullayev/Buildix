using System.Drawing.Printing;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Buildix.Desktop;

/// <summary>
/// Tayyor ESC/POS baytlarini chek printeriga yetkazadi.
///
/// <para><b>Ikkita yo'l bor va ikkalasi ham kerak.</b> USB yoki umumiy
/// (shared) printerda IP umuman yo'q — u faqat Windows navbati orqali
/// ko'rinadi. Tarmoq printerida esa aksincha: uni Windows'ga qo'shish
/// shart emas va do'konlarning ko'pchiligida u qo'shilmagan ham — kassa
/// bilan printer bitta routerda turadi, xolos. Bitta yo'l bilan
/// cheklansak, do'konlarning yarmi chekni bosa olmasdi.</para>
///
/// <para>Qaysi yo'l — sozlamadagi matnning O'ZI aytadi: IP manzil
/// yozilgan bo'lsa TCP, aks holda Windows navbati. Windows printer
/// nomi hech qachon IP manzilga o'xshamaydi, ya'ni chalkashlik yo'q.</para>
/// </summary>
internal static class ReceiptOutput
{
    /// <summary>
    /// Termal printerlarning standart porti (RAW / JetDirect). Deyarli
    /// hamma model shu portni tinglaydi va uni o'zgartirish kerak emas.
    /// </summary>
    private const int RawPort = 9100;

    /// <summary>
    /// Ulanish kutish chegarasi.
    /// </summary>
    /// <remarks>
    /// Ataylab qisqa: printer o'chiq bo'lsa TCP ulanishi sukut bo'yicha
    /// yigirma soniyagacha kutadi va shu vaqt ichida kassir «tugma
    /// ishlamayapti» deb yana bosaverardi.
    /// </remarks>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Baytlarni yozish chegarasi — chek bir necha kilobayt, xolos.</summary>
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Yuboradi; muvaffaqiyatli bo'lsa <c>null</c>, aks holda kassirga
    /// ko'rsatiladigan sabab.
    /// </summary>
    public static async Task<string?> SendAsync(string target, byte[] data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target)) return "Chek printeri tanlanmagan.";
        if (data.Length == 0) return "Chek ma'lumoti bo'sh.";

        return IsNetwork(target, out var host, out var port)
            ? await SendOverTcpAsync(host, port, data, ct)
            : RawPrinter.Send(target, data);
    }

    /// <summary>
    /// Sozlamadagi matn tarmoq printerinikimi va uning manzili qanday.
    /// </summary>
    /// <remarks>
    /// <para>Ajratish belgisi — IP manzil. Windows printer nomi
    /// («XP-58», «EPSON TM-T20 Receipt») hech qachon to'rt sonli nuqtali
    /// manzilga o'xshamaydi, shuning uchun bitta maydonga ikkalasini ham
    /// yozish mumkin va texnikdan qo'shimcha tanlov so'ralmaydi.</para>
    ///
    /// <para>DNS nomi bilan ishlash uchun ataylab <c>tcp://</c> old
    /// qo'shimchasi qoldirilgan: «printer» degan nom ham printer navbati,
    /// ham kompyuter nomi bo'lishi mumkin va taxmin qilish xavfli.</para>
    /// </remarks>
    public static bool IsNetwork(string target, out string host, out int port)
    {
        host = string.Empty;
        port = RawPort;

        var text = target.Trim();
        var explicitTcp = text.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase);
        if (explicitTcp) text = text["tcp://".Length..].TrimEnd('/');

        // Port yozilgan bo'lsa ajratamiz: «192.168.1.50:9100».
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var p) && p is > 0 and <= 65535)
        {
            text = text[..colon];
            port = p;
        }

        if (!explicitTcp && !IPAddress.TryParse(text, out _)) return false;

        host = text.Trim();
        return host.Length > 0;
    }

    /// <summary>
    /// Baytlarni printerga to'g'ridan-to'g'ri, drayversiz yuboradi.
    /// </summary>
    /// <remarks>
    /// Bu eng qisqa yo'l: na spooler, na drayver, na rasterlash. Printer
    /// baytlarni o'qishi bilan qog'oz chiqa boshlaydi.
    /// </remarks>
    private static async Task<string?> SendOverTcpAsync(
        string host, int port, byte[] data, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            cts.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(host, port, cts.Token);

            // Chek kichik va bitta bo'lakda ketadi — Nagle algoritmi bu
            // yerda faqat kechikish qo'shardi.
            client.NoDelay = true;

            using var stream = client.GetStream();
            cts.CancelAfter(WriteTimeout);
            await stream.WriteAsync(data, cts.Token);
            await stream.FlushAsync(cts.Token);
            return null;
        }
        catch (OperationCanceledException)
        {
            return $"«{host}:{port}» printeri javob bermadi. Yoqilganini va tarmoqdaligini tekshiring.";
        }
        catch (SocketException ex)
        {
            return $"«{host}:{port}» printeriga ulanib bo'lmadi: {ex.SocketErrorCode}.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Sozlanmagan do'konda chek printerini NOMI bo'yicha topishga urinadi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kerak.</b> Chek printeri <c>--setup</c> oynasida
    /// tanlanadi va uni o'rnatuvchi texnik o'tkazib yuborishi mumkin —
    /// aynan shu bo'lgan ham. O'shanda chek qobiq orqali umuman
    /// bosilmasdi va brauzer yo'liga tushib, kassir Windows'ning
    /// tushunarsiz xatosini ko'rardi.</para>
    ///
    /// <para><b>Nega ehtiyotkor.</b> Noto'g'ri printerga chek yuborish —
    /// A4 qog'ozni bekorga sarflash. Shu sababli faqat nomi chek
    /// printeriga ochiq ishora qiladigan va YAGONA bo'lgan qurilma
    /// tanlanadi; ikkitasi topilsa taxmin qilinmaydi va kassirga
    /// sozlash kerakligi aytiladi.</para>
    ///
    /// <para>Topilgani saqlanmaydi: sozlama texnikning ongli tanlovi
    /// bo'lib qolishi kerak, taxmin esa faqat ish to'xtab qolmasligi
    /// uchun.</para>
    /// </remarks>
    public static string? Guess()
    {
        // Chek printerlarining nomlarida deyarli har doim shulardan biri bor.
        string[] hints =
        [
            "pos-", "pos58", "pos80", "pos printer", "posprinter",
            "thermal", "termal", "receipt", "чек", "esc/pos", "escpos",
            "xprinter", "xp-", "epson tm", "tm-t", "tm-u", "tm-m",
            "rongta", "gprinter", "zjiang", "sprt", "bixolon", "star tsp",
            "58mm", "80mm",
        ];

        // Virtual «printerlar» — ular hech qachon chek bosmaydi.
        string[] virtualNames = ["pdf", "xps", "onenote", "fax", "microsoft print", "send to"];

        try
        {
            var found = PrinterSettings.InstalledPrinters
                .Cast<string>()
                .Where(n => !virtualNames.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)))
                .Where(n => hints.Any(h => n.Contains(h, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            return found.Count == 1 ? found[0] : null;
        }
        catch (Exception)
        {
            // Printerlar ro'yxatini o'qib bo'lmasligi chop etishni
            // to'xtatmasligi kerak — sozlamadagi nom baribir ishlaydi.
            return null;
        }
    }

    /// <summary>
    /// Sozlash oynasidagi «Sinov cheki» uchun qisqa ESC/POS varaqasi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kerak.</b> Ilgari sozlamani tekshirishning yagona
    /// yo'li haqiqiy savdo qilish edi. Texnik do'kondan chiqib ketgach
    /// printer ishlamayotgani ma'lum bo'lardi va uni qaytarish kerak
    /// bo'lardi.</para>
    ///
    /// <para>Matn ataylab faqat ASCII: bu yerda kod sahifasi bilan
    /// ishlash keraksiz murakkablik bo'lardi, chek matni esa serverda
    /// yasaladi.</para>
    /// </remarks>
    public static byte[] TestSlip(int cols = 32)
    {
        var body = new List<byte>(256);
        void Raw(params byte[] cmd) => body.AddRange(cmd);
        void Line(string text = "") => body.AddRange(Encoding.ASCII.GetBytes(text + "\n"));

        Raw(0x1B, 0x40);          // ESC @ — holatni tozalash
        Raw(0x1B, 0x61, 0x01);    // markazga
        Raw(0x1B, 0x45, 0x01);    // qalin
        Line("BUILDIX");
        Raw(0x1B, 0x45, 0x00);
        Line("sinov cheki");
        Raw(0x1B, 0x61, 0x00);    // chapga
        Line(new string('=', cols));
        // Ustunlar shu yerda ham tekshiriladi: chap va o'ng qism
        // bir-biriga yopishib qolsa, buni qog'ozdan darhol ko'rish mumkin.
        Line("Chap".PadRight(cols - 4) + "O'ng");
        Line(new string('-', cols));
        Line("JAMI".PadRight(cols - 7) + "451 000");
        Line(new string('=', cols));
        Raw(0x1B, 0x61, 0x01);
        Line("Printer tayyor.");
        Raw(0x1B, 0x61, 0x00);
        Raw(0x1D, 0x56, 0x41, 0x04);   // GS V 65 4 — surib, qirqish

        return [.. body];
    }
}
