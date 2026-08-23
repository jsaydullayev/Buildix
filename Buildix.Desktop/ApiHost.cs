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
    private Process? _process;
    private readonly SafeJob _job = new();

    public ApiHost(int port) => _port = port;

    public string BaseUrl => $"http://127.0.0.1:{_port}";

    /// <summary>Nashr papkasidagi API. Ishlab chiqishda ham, o'rnatilgandan keyin ham bir xil joyda.</summary>
    private static string ExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "api", "Buildix.API.exe");

    public bool ApiExists => File.Exists(ExecutablePath);

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
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Buildix.API ishga tushmadi.");
        _job.Attach(_process);
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
        if (IsFree(preferred)) return preferred;
        for (var p = preferred + 1; p < preferred + 50; p++)
            if (IsFree(p)) return p;
        throw new InvalidOperationException("Bo'sh port topilmadi.");

        static bool IsFree(int port)
        {
            try
            {
                using var l = new TcpListener(System.Net.IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch (SocketException) { return false; }
        }
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
        _job.Dispose();
    }
}
