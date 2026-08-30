using System.Diagnostics;
using System.Net.Sockets;

namespace Buildix.Desktop;

/// <summary>
/// Buildix.API ni bola-jarayon sifatida ishga tushiradi va uning umrini
/// oynaning umriga bog'laydi.
///
/// <para><b>Nega alohida jarayon.</b> API ni shu ilova ichida ham ishga
/// tushirsa bo'lardi, lekin uning kirish nuqtasi 900 qatorlik top-level
/// kod — uni chaqiriladigan holga keltirish katta va xavfli qayta yozish
/// bo'lardi. Alohida jarayon esa qo'shimcha foyda beradi: API kutilmaganda
/// yiqilsa oyna buni ko'radi va sababini ayta oladi, aksincha emas.</para>
///
/// <para><b>Yetim jarayon muammosi.</b> Oyna yopilganda API ni o'ldirish
/// yetarli emas: ilova qulab tushsa u orqada qolib ketadi va keyingi ishga
/// tushishda port band bo'ladi. Shuning uchun bola Job Object ga bog'lanadi
/// — Windows ota jarayon tugashi bilan uni o'zi yopadi.</para>
/// </summary>
public sealed class ApiHost : IAsyncDisposable
{
    private readonly int _port;
    private readonly SafeJob _job;
    private Process? _process;
    private TextWriter? _log;

    public ApiHost(int port, SafeJob job)
    {
        _port = port;
        _job = job;
    }

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>API tinglayotgan port — boshqa kassalar aynan shunga ulanadi.</summary>
    public int Port => _port;

    /// <summary>
    /// API ni lokal tarmoqqa ochish (2- va 3-kassa uchun). Qiymat
    /// kompyuterning o'z sozlamasidan keladi, nashr faylidan emas — nashr
    /// fayli har yangilanishda almashadi.
    /// </summary>
    public bool AllowLan { get; set; }

    /// <summary>Nashr papkasidagi API. Ishlab chiqishda ham, o'rnatilgandan keyin ham bir xil joyda.</summary>
    private static string ExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "api", "Buildix.API.exe");

    public bool ApiExists => File.Exists(ExecutablePath);

    /// <summary>Baza ulanish satri — qobiq tomonidan beriladi.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Bulut manzili va shu kompyuterning kaliti — qobiqdan.</summary>
    public string? CloudUrl { get; set; }
    public string? TerminalKey { get; set; }

    public void Start()
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Desktop";
        // Tarmoqqa ochilganda API 0.0.0.0 ni o'zi tanlaydi (Program.cs), shu
        // sababli bu yerda faqat port muhim — manzilni belgilab qo'yish uni
        // qayta loopback'ga qamab qo'yardi.
        psi.Environment["ASPNETCORE_URLS"] = AllowLan ? $"http://0.0.0.0:{_port}" : BaseUrl;
        psi.Environment["Desktop__AllowLan"] = AllowLan ? "true" : "false";
        // Ulanish satri muhit o'zgaruvchisi orqali: fayldagi sozlamada parol
        // ochiq yotmasin va uni tasodifan nusxalab yuborish imkoni bo'lmasin.
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            psi.Environment["ConnectionStrings__DefaultConnection"] = ConnectionString;

        // Bulut kaliti ham AYNAN shu yo'l bilan. Uni API o'qiydigan sozlama
        // fayliga yozish mumkin edi, lekin unda kalit diskda ikkinchi marta,
        // huquqlari cheklanmagan joyda yotardi. Sirlar faylining yagona
        // egasi — qobiq.
        if (!string.IsNullOrWhiteSpace(CloudUrl) && !string.IsNullOrWhiteSpace(TerminalKey))
        {
            psi.Environment["Cloud__Url"] = CloudUrl;
            psi.Environment["Cloud__TerminalKey"] = TerminalKey;
        }

        // API ning chiqishi faylga yoziladi. Busiz u hech qayerga bormasdi:
        // jarayon oynasiz ishga tushadi va ishga tushishda yiqilsa, do'konda
        // «API javob bermadi» degan umumiy xabardan boshqa hech narsa
        // qolmasdi — sababini aniqlashning yo'li yo'q edi.
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        // API o'zbekcha xabarlar yozadi va ular UTF-8 da. Kodlash
        // ko'rsatilmasa .NET oqimni tizim kod sahifasida o'qir va harflar
        // faylda tanib bo'lmas holga kelardi.
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Buildix.API ishga tushmadi.");

        // Oqimlar ASINXRON o'qiladi. Sinxron o'qish (yoki umuman o'qimaslik)
        // quvur to'lganda bola jarayonni muzlatib qo'yardi — u yozmoqchi
        // bo'ladi, hech kim o'qimaydi va API abadiy kutib qoladi.
        // Ikki oqim (stdout va stderr) IKKI XIL ipdan yozadi va StreamWriter
        // buni ko'tarmaydi — yozuvlar bir-biriga aralashib, jurnalni
        // o'qib bo'lmas holga keltirardi yoki istisno tashlardi.
        _log = TextWriter.Synchronized(
            new StreamWriter(SecretFile.RotateLog(LogPath), append: false, new System.Text.UTF8Encoding(true))
            { AutoFlush = true });
        _process.OutputDataReceived += WriteLine;
        _process.ErrorDataReceived += WriteLine;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _job.Attach(_process);
    }

    /// <summary>API jurnali — do'konda muammoni aniqlashning yagona izi.</summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Buildix", "api.log");

    private void WriteLine(object _, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        try { _log?.WriteLine(e.Data); }
        catch (IOException) { /* jurnal yozilmasa ham API ishlayversin */ }
        catch (ObjectDisposedException) { /* yopilish paytida */ }
    }

    /// <summary>API javob berguncha kutadi. Bermasa — sababini aytadi.</summary>
    public async Task<string?> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
                return $"Buildix.API to'xtab qoldi (kod {_process.ExitCode}). Jurnalni tekshiring.";

            try
            {
                var r = await http.GetAsync($"{BaseUrl}/health", ct);
                if (r.IsSuccessStatusCode) return null;
            }
            catch (HttpRequestException) { /* hali ko'tarilmagan */ }
            catch (TaskCanceledException) { /* javob kechikdi */ }

            await Task.Delay(250, ct);
        }
        return "Buildix.API belgilangan vaqtda javob bermadi.";
    }

    /// <summary>Band bo'lmagan port topadi — ikkinchi nusxa yoki boshqa dastur to'sib qo'ymasin.</summary>
    public static int FindFreePort(int preferred)
    {
        if (IsPortFree(preferred)) return preferred;
        for (var p = preferred + 1; p < preferred + 50; p++)
            if (IsPortFree(p)) return p;
        throw new InvalidOperationException("Bo'sh port topilmadi.");
    }

    /// <summary>
    /// Port bo'shmi.
    /// </summary>
    /// <param name="lan">
    /// Tarmoqqa ochilgan kassa uchun <c>true</c>: tekshiruv HAMMA
    /// interfeysda bajariladi. Port loopback'da bo'sh, tashqi interfeysda
    /// esa band bo'lishi mumkin — o'shanda API ishga tushmas, sabab esa
    /// «bo'sh port topildi» degan xulosaga zid bo'lardi.
    /// </param>
    public static bool IsPortFree(int port, bool lan = false)
    {
        try
        {
            using var l = new TcpListener(
                lan ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch (InvalidOperationException) { /* allaqachon tugagan */ }
        }
        _process?.Dispose();
        _log?.Dispose();
    }
}
