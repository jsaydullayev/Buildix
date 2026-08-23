using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Buildix.Desktop;

/// <summary>
/// To'plam ichidagi PostgreSQL ni boshqaradi.
///
/// <para><b>Nega Windows xizmati emas.</b> Xizmat sifatida o'rnatish
/// administrator huquqini talab qiladi va do'kondagi kompyuterda bu har doim
/// ham bo'lmaydi. Bu yerda esa PostgreSQL oddiy bola jarayon sifatida
/// ishlaydi: huquq kerak emas, o'rnatish yo'q, va u ham API bilan bir xil
/// Job Object ga bog'lanadi — ilova qulasa baza ham to'xtaydi va keyingi
/// ishga tushishda «port band» yoki «baza qulflangan» xatosi chiqmaydi.</para>
///
/// <para><b>Xavfsizlik.</b> Baza faqat 127.0.0.1 da tinglaydi — tarmoqdan
/// unga umuman ulanib bo'lmaydi. Parol birinchi ishga tushishda yaratiladi
/// va shu kompyuterda qoladi, ya'ni har do'konda boshqacha.</para>
/// </summary>
public sealed class PostgresHost : IAsyncDisposable
{
    private const string DbUser = "buildix";
    private const string DbName = "buildix";

    private readonly SafeJob _job;
    private Process? _process;
    private int _port;
    private string _password = "";

    public PostgresHost(SafeJob job) => _job = job;

    /// <summary>To'plamdagi PostgreSQL. Nashr papkasida `pg/` ichida yotadi.</summary>
    private static string Root => Path.Combine(AppContext.BaseDirectory, "pg");
    private static string Bin(string exe) => Path.Combine(Root, "bin", exe + ".exe");

    /// <summary>Xabarda ko'rsatiladigan yo'l.</summary>
    private static string SecretsHint => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Buildix", "desktop.json");

    private static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Buildix", "pgdata");

    public bool IsBundled => File.Exists(Bin("postgres")) && File.Exists(Bin("initdb"));

    public string ConnectionString =>
        $"Host=127.0.0.1;Port={_port};Database={DbName};Username={DbUser};Password={_password};" +
        "Include Error Detail=true";

    /// <summary>
    /// Bazani tayyorlaydi va ko'taradi. Xato bo'lsa — foydalanuvchiga
    /// ko'rsatiladigan sabab qaytadi, aks holda null.
    /// </summary>
    public async Task<string?> StartAsync(
        Func<string, string> secret, bool secretsAreNew, CancellationToken ct)
    {
        _password = secret("Database:Password");
        _port = ApiHost.FindFreePort(5433);   // 5432 — tizimdagi Postgres band qilishi mumkin

        var firstRun = !Directory.Exists(Path.Combine(DataDir, "base"));

        // Baza bor, lekin paroli yo'q — bu holatdan chiqish yo'li yo'q:
        // parol bazada faqat hash ko'rinishida saqlanadi. Buni oldindan
        // aytmasak, PostgreSQL ning «проверка подлинности не пройдена»
        // xabari chiqadi va omborchi nima qilishni bilmaydi.
        if (!firstRun && secretsAreNew)
        {
            return "Ma'lumotlar bazasi mavjud, lekin uning paroli topilmadi."
                + Environment.NewLine + Environment.NewLine
                + $"desktop.json fayli yo'qolgan yoki o'chirilgan: {SecretsHint}"
                + Environment.NewLine + Environment.NewLine
                + "Uni zaxira nusxadan tiklang. Fayl tiklanmasa, mavjud bazani ochib bo'lmaydi.";
        }

        if (firstRun)
        {
            var error = await InitialiseAsync(ct);
            if (error is not null) return error;
        }

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = Bin("postgres"),
            // listen_addresses — tarmoqqa chiqmaslikning asosiy kafolati.
            Arguments = $"-D \"{DataDir}\" -p {_port} -c listen_addresses=127.0.0.1 -c logging_collector=off",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        });
        if (_process is null) return "PostgreSQL ishga tushmadi.";
        _job.Attach(_process);

        if (!await WaitReadyAsync(TimeSpan.FromSeconds(60), ct))
            return "PostgreSQL belgilangan vaqtda javob bermadi.";

        // Baza HAR SAFAR tekshiriladi, faqat birinchi ishga tushishda emas.
        // Ilgari `firstRun` bayrog'iga tayanardi: initdb muvaffaqiyatli
        // tugab, createdb esa o'tmay qolsa (masalan vaqtinchalik xato),
        // keyingi ishga tushishlarda pgdata mavjud bo'lgani uchun baza umuman
        // yaratilmasdi va ilova «baza topilmadi» bilan abadiy ochilmay
        // qolardi — yagona chora pgdata ni o'chirish, ya'ni hamma narsani
        // yo'qotish edi.
        return await EnsureDatabaseAsync(ct);
    }

    /// <summary>Birinchi ishga tushish: bo'sh ma'lumotlar katalogini yaratadi.</summary>
    private async Task<string?> InitialiseAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(DataDir);

        // Parolni buyruq qatoriga yozib bo'lmaydi — u jarayonlar ro'yxatida
        // ko'rinadi. initdb uni fayldan o'qiydi, fayl esa darhol o'chiriladi.
        var pwFile = Path.Combine(Path.GetTempPath(), $"bx-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(pwFile, _password, new UTF8Encoding(false), ct);
        try
        {
            var (code, err) = await RunAsync(Bin("initdb"),
                $"-D \"{DataDir}\" -U {DbUser} --pwfile=\"{pwFile}\" -E UTF8 " +
                "--auth-local=trust --auth-host=scram-sha-256", ct);
            if (code != 0) return "Ma'lumotlar bazasini yaratib bo'lmadi.\n\n" + err;
        }
        finally
        {
            try { File.Delete(pwFile); } catch (IOException) { /* keyingi tozalashda ketadi */ }
        }
        return null;
    }

    /// <summary>Baza bor bo'lsa hech narsa qilmaydi, yo'q bo'lsa yaratadi.</summary>
    private async Task<string?> EnsureDatabaseAsync(CancellationToken ct)
    {
        var (code, err) = await RunAsync(Bin("createdb"),
            $"-h 127.0.0.1 -p {_port} -U {DbUser} {DbName}", ct, _password);
        if (code == 0) return null;
        // «allaqachon mavjud» — xato emas, kutilgan holat.
        if (err.Contains("already exists", StringComparison.OrdinalIgnoreCase)) return null;
        return "Ma'lumotlar bazasi yaratilmadi." + Environment.NewLine + Environment.NewLine + err;
    }

    private async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true }) return false;
            var (code, _) = await RunAsync(Bin("pg_isready"), $"-h 127.0.0.1 -p {_port}", ct);
            if (code == 0) return true;
            await Task.Delay(300, ct);
        }
        return false;
    }

    private static async Task<(int Code, string Error)> RunAsync(
        string exe, string args, CancellationToken ct, string? password = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        if (password is not null) psi.Environment["PGPASSWORD"] = password;

        using var p = Process.Start(psi);
        if (p is null) return (-1, $"{Path.GetFileName(exe)} ishga tushmadi.");
        var err = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, err);
    }

    /// <summary>Yangi tasodifiy parol — birinchi ishga tushish uchun.</summary>
    public static string NewPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("/", "_").Replace("+", "-");

    public async ValueTask DisposeAsync()
    {
        if (_process is not { HasExited: false }) { _process?.Dispose(); return; }

        // Toza to'xtatish: ma'lumot diskka yozilsin. Ulgurmasa — Job Object
        // uni baribir yopadi, lekin unda keyingi ishga tushish sekinroq
        // bo'ladi (tiklash jurnali o'qiladi).
        try
        {
            await RunAsync(Bin("pg_ctl"), $"-D \"{DataDir}\" -m fast stop", CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
        _process.Dispose();
    }
}
