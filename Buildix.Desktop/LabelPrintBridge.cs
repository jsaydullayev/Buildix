using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

namespace Buildix.Desktop;

/// <summary>
/// Yorliq va chekni printerga TO'G'RIDAN-TO'G'RI chiqaradi — chop etish
/// oynasisiz.
/// </summary>
/// <remarks>
/// <para><b>Ikki yo'l.</b> Yorliq (va zaxira sifatida chek rasmi) sahifadan
/// chiziladi; chekning o'zi esa tayyor ESC/POS baytlari bo'lib, XOM holda
/// printerga o'tadi va u yerda hech qanday rasterlash bo'lmaydi.</para>
///
/// <para><b>Muammo.</b> Yorliq brauzerning chop etish oynasi orqali bosilardi.
/// U sukut bo'yicha «sahifaga moslash» qiladi va sukutdagi printerni tanlaydi:
/// 58×40 mm maket Windows'dagi A4 qog'ozga cho'zilib ketardi, omborchi esa har
/// safar printerni qo'lda almashtirishga majbur bo'lardi.</para>
///
/// <para><b>Yechim.</b> Do'kon ilovasida oyna umuman kerak emas. Sahifa
/// ko'rinmas WebView2 da ochiladi va <c>PrintAsync</c> bilan bosiladi: qog'oz
/// o'lchami aynan yorliq o'lchami, chekka nol, masshtab yo'q, printer esa
/// sozlamada bir marta tanlangan.</para>
///
/// <para><b>Nega baribir sahifadan chizamiz.</b> Maket bitta joyda —
/// serverda — qoladi. Bu yerda faqat qog'oz parametrlari qo'yiladi.</para>
///
/// <para>Printer sozlanmagan yoki chop etish yiqilgan bo'lsa, ish
/// TO'XTAMAYDI: sahifaga «oynani o'zing och» degan javob qaytadi va u
/// brauzerdagi odatdagi yo'lga tushadi.</para>
/// </remarks>
public sealed class LabelPrintBridge
{
    /// <summary>Sahifa yuboradigan xabar turi.</summary>
    private const string RequestKind = "buildix.print-labels";

    /// <summary>
    /// Tayyor baytlarni printerga XOM holda yuborish (ESC/POS chek).
    /// </summary>
    private const string RawKind = "buildix.print-raw";

    private readonly LocalSecrets _secrets;
    private readonly CoreWebView2Environment _environment;

    public LabelPrintBridge(LocalSecrets secrets, CoreWebView2Environment environment)
    {
        _secrets = secrets;
        _environment = environment;
    }

    /// <summary>
    /// Interfeys oynasiga ko'prikni ulaydi va uni sahifaga tanitadi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kutiladi.</b> Skript ro'yxatdan o'tkazish asinxron, va
    /// chaqiruvchi darhol manzilni qo'yadi. Ilgari bu KUTILMASDI: ro'yxatga
    /// olish sahifa yuklanishidan kechiksa, <c>window.buildixDesktop</c>
    /// birinchi hujjatda umuman bo'lmasdi. O'shanda sahifa o'zini brauzerda
    /// deb hisoblar va chekni chop etish oynasiga yuborardi — aynan qochmoqchi
    /// bo'lgan xulq.</para>
    /// </remarks>
    public async Task AttachAsync(CoreWebView2 core)
    {
        // Sahifa qobiq ichida ekanini SHU belgi bilan biladi. Brauzerda bu
        // yo'q va u odatdagi chop etish oynasidan foydalanadi.
        // Kassa belgisi ham shu yerda beriladi: sahifa uni har so'rovga
        // qo'shadi va server chek qaysi kassada urilganini shundan biladi.
        // Lokal tarmoq rejimida boshqa yo'l yo'q — 2-kassaning o'z API si
        // yo'q, ya'ni belgini server tomonidan aniqlab bo'lmaydi.
        var register = JsonSerializer.Serialize(_secrets.RegisterCode);

        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.buildixDesktop = Object.assign(window.buildixDesktop || {}, "
            + "{ canPrintLabels: true, canPrintReceipts: true, canPrintRaw: true, "
            + $"registerCode: {register} }});");

        core.WebMessageReceived += async (_, e) =>
        {
            PrintRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<PrintRequest>(e.WebMessageAsJson);
            }
            catch (JsonException)
            {
                return;   // Bizga tegishli bo'lmagan xabar.
            }

            if (request is null) return;

            // XOM yo'l — ESC/POS chek. Rasterlash umuman yo'q: baytlar
            // to'g'ridan-to'g'ri printerga boradi va qog'oz darhol chiqadi.
            if (request.Kind == RawKind)
            {
                var rawProblem = await PrintRawAsync(request);
                core.PostWebMessageAsJson(JsonSerializer.Serialize(new PrintResult(
                    RawKind + ".result", request.Id, rawProblem is null, rawProblem)));
                return;
            }

            if (request.Kind != RequestKind) return;

            var problem = await PrintAsync(request);
            // Natija sahifaga qaytadi: muvaffaqiyatsiz bo'lsa u o'zi oyna
            // ochadi. Jimgina yutib yuborilsa, omborchi tugmani bosib
            // hech narsa bo'lmaganini ko'rardi.
            core.PostWebMessageAsJson(JsonSerializer.Serialize(new PrintResult(
                RequestKind + ".result", request.Id, problem is null, problem)));
        };
    }

    /// <summary>Tayyor baytlarni printerga yuboradi.</summary>
    private async Task<string?> PrintRawAsync(PrintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DataBase64)) return "Chek ma'lumoti bo'sh.";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.DataBase64);
        }
        catch (FormatException)
        {
            return "Chek ma'lumoti buzilgan.";
        }

        var printer = await ReceiptTargetAsync();
        if (printer is null)
            return "Chek printeri tanlanmagan. Buildix.Desktop.exe --setup oynasida tanlang.";

        return await ReceiptOutput.SendAsync(printer, bytes, CancellationToken.None);
    }

    /// <summary>
    /// Chek qayerga chiqadi: sozlamadagi printer, u yo'q bo'lsa — nomi
    /// bo'yicha topilgani. UI oqimini BLOKLAMAYDI.
    /// </summary>
    /// <remarks>
    /// <para>Sozlamani o'tkazib yuborish oson va oqibati og'ir: chek qobiq
    /// orqali umuman bosilmas, brauzer yo'liga tushar va kassir Windows'ning
    /// «bu havolani ochadigan dastur yo'q» xatosini ko'rardi. Taxmin
    /// ehtiyotkor — <see cref="ReceiptOutput.Guess"/> ga qarang.</para>
    ///
    /// <para><see cref="ReceiptOutput.Guess"/> Windows'dagi printerlar
    /// ro'yxatini o'qiydi — o'chiq tarmoq printeri bo'lsa bu chaqiruv sekin
    /// tugaydi. U UI oqimida bajarilsa oyna muzlab qolardi. Sinxron egizagi
    /// ATAYLAB yo'q: u qolganda keyingi tahrir noto'g'risini tanlar va
    /// muzlash aynan zaxira (rasm) yo'lida qaytardi — ya'ni printer
    /// sozlanmagan kassada, birinchi chekda.</para>
    /// </remarks>
    private Task<string?> ReceiptTargetAsync() =>
        _secrets.ReceiptPrinter is { } configured
            ? Task.FromResult<string?>(configured)
            : Task.Run(() => ReceiptOutput.Guess());

    /// <summary>Bosadi; muvaffaqiyatli bo'lsa <c>null</c>, aks holda sabab.</summary>
    private async Task<string?> PrintAsync(PrintRequest request)
    {
        // Chek va yorliq — ODATDA IKKI XIL printer: biri rulonli, ikkinchisi
        // etiket. Bitta sozlama bilan chek etiket printeriga tushardi va
        // 58x40 mm yorliqqa bosilgan chek hech narsaga yaramasdi.
        var receipt = string.Equals(request.Target, "receipt", StringComparison.OrdinalIgnoreCase);
        var printer = receipt ? await ReceiptTargetAsync() : _secrets.LabelPrinter;
        if (printer is null)
            return receipt ? "Chek printeri tanlanmagan." : "Yorliq printeri tanlanmagan.";

        // Tarmoq printeriga (IP:9100) sahifa yo'li bilan bosib bo'lmaydi:
        // WebView2 Windows NAVBATINI talab qiladi, IP manzil esa navbat
        // emas. Bu yerda ochiq to'xtaymiz — aks holda drayver «printer
        // topilmadi» deb, sababi butunlay boshqa xatoni qaytarardi.
        if (receipt && ReceiptOutput.IsNetwork(printer, out _, out _))
            return "Tarmoq printeriga faqat ESC/POS yo'li bilan chop etiladi.";

        if (request.WidthMm <= 0 || request.HeightMm <= 0)
            return receipt ? "Chek o'lchami noto'g'ri." : "Yorliq o'lchami noto'g'ri.";

        // Sahifa yo'li HTML talab qiladi; XOM yo'lda u bo'lmaydi va bu
        // yerga umuman kelmasligi kerak.
        if (string.IsNullOrEmpty(request.Html)) return "Chop etiladigan sahifa bo'sh.";

        CoreWebView2Controller? controller = null;
        try
        {
            // Ko'rinmas oyna: hujjat faqat chop etish uchun chiziladi.
            // `Bounds` nolga teng bo'lsa WebView2 render qilmaydi, shuning
            // uchun kichik lekin haqiqiy o'lcham beriladi.
            controller = await _environment.CreateCoreWebView2ControllerAsync(HiddenHost.Handle);
            controller.IsVisible = false;
            controller.Bounds = new Rectangle(0, 0, 400, 300);

            var core = controller.CoreWebView2;
            var ready = new TaskCompletionSource();
            void OnCompleted(object? _, CoreWebView2NavigationCompletedEventArgs __) => ready.TrySetResult();
            core.NavigationCompleted += OnCompleted;

            core.NavigateToString(request.Html);

            // Rasm yuklanmay chop etilsa qog'ozdan BO'SH yorliq chiqardi.
            var loaded = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            core.NavigationCompleted -= OnCompleted;
            if (loaded != ready.Task) return "Yorliq sahifasi yuklanmadi.";

            var settings = _environment.CreatePrintSettings();
            settings.PrinterName = printer;
            // Dyuymda — WebView2 boshqa birlikni bilmaydi.
            settings.PageWidth = request.WidthMm / 25.4;
            settings.PageHeight = request.HeightMm / 25.4;
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            // Masshtab AYNAN 1: «sahifaga moslash» aynan shu yerda o'chadi.
            settings.ScaleFactor = 1.0;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;

            var status = await core.PrintAsync(settings);
            return status switch
            {
                CoreWebView2PrintStatus.Succeeded => null,
                CoreWebView2PrintStatus.PrinterUnavailable => $"«{printer}» printeri topilmadi yoki band.",
                _ => "Chop etib bo'lmadi.",
            };
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            controller?.Close();
        }
    }

    /// <summary>
    /// Ko'rinmas WebView2 uchun ota-oyna.
    ///
    /// <para>WebView2 kontrolleri HWND talab qiladi. Asosiy oynaga ulash
    /// mumkin emas: u yopilgan bo'lishi mumkin, va yopilish paytida chop
    /// etish yarim qolardi.</para>
    /// </summary>
    private static Form HiddenHost { get; } = CreateHiddenHost();

    private static Form CreateHiddenHost()
    {
        var form = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            // Ekrandan tashqarida — hech qachon ko'rinmaydi.
            Location = new Point(-32000, -32000),
            Size = new Size(400, 300),
        };
        // Handle DARHOL yaratiladi: WebView2 kontrolleriga u kerak.
        _ = form.Handle;
        return form;
    }

    private sealed record PrintRequest(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("html")] string? Html,
        [property: JsonPropertyName("widthMm")] double WidthMm,
        [property: JsonPropertyName("heightMm")] double HeightMm,
        /// <summary>XOM yo'l uchun: base64 dagi tayyor baytlar.</summary>
        [property: JsonPropertyName("dataBase64")] string? DataBase64 = null,
        /// <summary>«label» (sukut) yoki «receipt» — qaysi printerga.</summary>
        [property: JsonPropertyName("target")] string? Target = null);

    private sealed record PrintResult(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("problem")] string? Problem);
}
