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
    private StreamWriter? _log;
    private int _port;
    private string _password = "";

    /// <summary>Baza jurnali — tiklanish yoki buzilish sababi shu yerda.</summary>
    public static string DbLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Buildix", "db.log");

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
            //
            // lc_messages=C — jurnal INGLIZ tilida bo'lsin. Sababi kodlash:
            // PostgreSQL xabarlarni tizim tilida va tizim kod sahifasida
            // yozadi, .NET esa oqimni boshqacha o'qiydi va rus tilidagi matn
            // faylda «РЎРћРћР‘Р©Р•РќРР•» ko'rinishida chiqib qoladi — ya'ni
            // jurnal umuman o'qib bo'lmas holga keladi. Ingliz tilidagi
            // xabarlar sof ASCII va hech qanday kodlash muammosi tug'dirmaydi;
            // qidiruvda ham aynan ular topiladi.
            Arguments = $"-D \"{DataDir}\" -p {_port} -c listen_addresses=127.0.0.1 " +
                        "-c logging_collector=off -c lc_messages=C",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        });
        if (_process is null) return "PostgreSQL ishga tushmadi.";

        // Bazaning chiqishi faylga yoziladi. Ikki sabab bor va ikkalasi ham
        // jiddiy. Birinchisi: `logging_collector=off` bo'lgani uchun PostgreSQL
        // HAMMA narsani stderr ga yozadi va u hech kim o'qimaydigan quvurga
        // ketardi — quvur to'lgach baza yozolmay MUZLAB qolardi. Ikkinchisi:
        // tiklanish, buzilgan fayl yoki joy yetishmasligi kabi sabablar faqat
        // shu yerda ko'rinadi; ularsiz do'kondan «ochilmayapti» degandan
        // boshqa hech qanday ma'lumot kelmasdi.
        _log = new StreamWriter(DbLogPath, append: false, new UTF8Encoding(true)) { AutoFlush = true };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            try { _log?.WriteLine(e.Data); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        };
        _process.BeginErrorReadLine();

        _job.Attach(_process);

        // 3 daqiqa: elektr uzilgandan keyin tiklanish katta bazada bir necha
        // daqiqa davom etishi mumkin va uni yarim yo'lda to'xtatish mumkin emas.
        if (!await WaitReadyAsync(TimeSpan.FromMinutes(3), ct))
            return "Ma'lumotlar bazasi belgilangan vaqtda tayyor bo'lmadi."
                + Environment.NewLine + Environment.NewLine
                + "Agar elektr yaqinda uzilgan bo'lsa, baza o'zini tiklayotgan bo'lishi "
                + "mumkin — bir necha daqiqadan keyin qaytadan urinib ko'ring."
                + Environment.NewLine + Environment.NewLine
                + "Batafsil: " + DbLogPath;

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
            var (code, err, _) = await RunAsync(Bin("initdb"),
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

    /// <summary>
    /// Baza bor bo'lsa hech narsa qilmaydi, yo'q bo'lsa yaratadi.
    ///
    /// <para><b>Xato MATNI hech qachon o'qilmaydi.</b> Oldingi variant
    /// <c>createdb</c> ning javobida «already exists» borligini tekshirardi.
    /// PostgreSQL xabarlarni TIZIM TILIDA beradi: rus tilidagi Windows'da
    /// o'sha xabar «база данных "buildix" уже существует» bo'lib chiqadi va
    /// tekshiruv mos kelmaydi. Natijada mavjud baza xato deb qabul qilinar,
    /// ilova esa API ni umuman ishga tushirmasdan to'xtardi — ya'ni do'kon
    /// IKKINCHI ochilishdayoq ishlamay qolardi. O'zbekistondagi kompyuterlarda
    /// tizim tili odatda rus tili, ya'ni bu deyarli har do'konda yuz berardi.
    /// Endi mavjudlik SO'ROV bilan aniqlanadi va til ahamiyatsiz.</para>
    /// </summary>
    private async Task<string?> EnsureDatabaseAsync(CancellationToken ct)
    {
        if (await DatabaseExistsAsync(ct)) return null;

        var (code, err, _) = await RunAsync(Bin("createdb"),
            $"-h 127.0.0.1 -p {_port} -U {DbUser} {DbName}", ct, _password);
        if (code == 0) return null;

        // Yaratib bo'lmadi. Yana bir bor tekshiramiz: ikkinchi kassa yoki
        // oldingi jarayon shu orada yaratib qo'ygan bo'lishi mumkin.
        if (await DatabaseExistsAsync(ct)) return null;

        return "Ma'lumotlar bazasi yaratilmadi." + Environment.NewLine + Environment.NewLine + err;
    }

    /// <summary>Baza mavjudligini so'rov bilan aniqlaydi — matn tahlilisiz.</summary>
    private async Task<bool> DatabaseExistsAsync(CancellationToken ct)
    {
        // `postgres` — initdb yaratadigan xizmat bazasi, u har doim bor.
        var (code, _, output) = await RunAsync(Bin("psql"),
            $"-h 127.0.0.1 -p {_port} -U {DbUser} -d postgres -tAc " +
            $"\"select 1 from pg_database where datname='{DbName}'\"", ct, _password);
        return code == 0 && output.Trim() == "1";
    }

    /// <summary>
    /// Baza HAQIQATAN so'rov qabul qilguncha kutadi.
    ///
    /// <para><b>Nega <c>pg_isready</c> emas.</b> U TCP darajasida javob
    /// berayotganini ko'rsatadi, xolos. Elektr uzilgandan keyin (yoki jarayon
    /// majburan to'xtatilganda) PostgreSQL tiklanish rejimida ishga tushadi:
    /// ulanishni qabul qiladi, lekin har bir so'rovga «ma'lumotlar bazasi
    /// tizimi ishga tushmoqda» deb javob beradi. Shu paytda davom etilsa,
    /// keyingi qadam xato bilan yiqilar va ilova «baza yaratilmadi» degan
    /// noto'g'ri xabar bilan to'xtardi — aslida baza joyida, u shunchaki
    /// tiklanayotgan bo'lardi. Do'konda bu har bir elektr uzilishidan keyin
    /// takrorlanardi.</para>
    ///
    /// <para>Haqiqiy so'rov esa faqat baza to'liq tayyor bo'lgandagina
    /// muvaffaqiyatli tugaydi. Xato MATNI o'qilmaydi — faqat chiqish kodi va
    /// natija, ya'ni tizim tili ahamiyatsiz.</para>
    /// </summary>
    private async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true }) return false;

            var (code, _, output) = await RunAsync(Bin("psql"),
                $"-h 127.0.0.1 -p {_port} -U {DbUser} -d postgres -tAc \"select 1\"",
                ct, _password);
            if (code == 0 && output.Trim() == "1") return true;

            await Task.Delay(500, ct);
        }
        return false;
    }

    private static async Task<(int Code, string Error, string Output)> RunAsync(
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
        if (p is null) return (-1, $"{Path.GetFileName(exe)} ishga tushmadi.", "");

        // IKKALA oqim ham BIR VAQTDA o'qiladi. Ilgari faqat stderr o'qilardi:
        // stdout ga ko'p yozadigan buyruq quvur to'lganda muzlab qolar va
        // ReadToEnd hech qachon tugamas edi — ilova esa hech qanday xato
        // bermasdan abadiy kutib turardi.
        var errTask = p.StandardError.ReadToEndAsync(ct);
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        await Task.WhenAll(errTask, outTask);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, errTask.Result, outTask.Result);
    }

    /// <summary>Zaxira nusxalar papkasi.</summary>
    public static string BackupDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Buildix", "backups");

    /// <summary>Kuniga bir marta — shundan tez-tez nusxa olishning ma'nosi yo'q.</summary>
    private static readonly TimeSpan BackupInterval = TimeSpan.FromHours(20);

    /// <summary>Necha kunlik tarix saqlanadi.</summary>
    private const int BackupsToKeep = 14;

    /// <summary>
    /// Kunlik zaxira nusxa oladi (kerak bo'lsa) va eskilarini o'chiradi.
    ///
    /// <para><b>Nimadan himoya qiladi.</b> Xato bilan o'chirilgan ma'lumot va
    /// buzilgan baza — ikkalasi ham daqiqalar ichida tiklanadi.
    /// <b>Nimadan himoya qilmaydi:</b> disk ishdan chiqishi yoki kompyuter
    /// o'g'irlanishi. Nusxa o'sha diskda yotadi, ya'ni bu yagona zaxira
    /// bo'lolmaydi — u bulutga sinxronizatsiyaning o'rnini bosmaydi.</para>
    ///
    /// <para><b>Nega jadval bo'yicha emas, ochilishda.</b> Do'kon kompyuteri
    /// kechasi o'chiriladi, ya'ni «har kuni soat 02:00 da» degan jadval hech
    /// qachon ishlamasdi. Ochilishda tekshirish esa ilova ishlatilayotgan har
    /// kuni bir marta bajarilishini kafolatlaydi.</para>
    ///
    /// <para><b>Nega fonda.</b> Nusxa olish bir necha soniya oladi va uni
    /// kutish savdoning boshlanishini kechiktirardi. Xato bo'lsa ham savdo
    /// to'xtamaydi — sabab jurnalga yoziladi.</para>
    /// </summary>
    public async Task BackupIfDueAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(BackupDir);

            var existing = new DirectoryInfo(BackupDir)
                .GetFiles("buildix-*.dump")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (existing.Count > 0 &&
                DateTime.UtcNow - existing[0].LastWriteTimeUtc < BackupInterval)
            {
                return;
            }

            // Vaqtinchalik nomga yoziladi va faqat MUVAFFAQIYATLI tugagach
            // haqiqiy nomga o'tkaziladi. Aks holda yarim yozilgan fayl
            // to'liq nusxadek ko'rinar va uni tiklashga urinilganda
            // ma'lumot yo'qligi ANIQ SHU PAYTDA bilinardi.
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var target = Path.Combine(BackupDir, $"buildix-{stamp}.dump");
            var temp = target + ".partial";

            var (code, err, _) = await RunAsync(Bin("pg_dump"),
                $"-h 127.0.0.1 -p {_port} -U {DbUser} -d {DbName} -Fc -f \"{temp}\"",
                ct, _password);

            if (code != 0)
            {
                try { File.Delete(temp); } catch (IOException) { }
                _log?.WriteLine($"[buildix] zaxira nusxa olinmadi: {err.Trim()}");
                return;
            }

            File.Move(temp, target, overwrite: true);
            _log?.WriteLine($"[buildix] zaxira nusxa: {target}");

            // Eskilarini o'chiramiz — aks holda papka cheksiz o'sib, do'kon
            // diskini to'ldirib qo'yardi.
            foreach (var old in existing.Skip(BackupsToKeep - 1))
            {
                try { old.Delete(); } catch (IOException) { }
            }
        }
        catch (Exception ex)
        {
            // Zaxira nusxa savdodan muhimroq emas: xato bo'lsa jurnalga
            // yoziladi va ilova ishlayveradi.
            try { _log?.WriteLine($"[buildix] zaxira nusxa xatosi: {ex.Message}"); }
            catch (Exception) { }
        }
    }

    /// <summary>Yangi tasodifiy parol — birinchi ishga tushish uchun.</summary>
    public static string NewPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace("/", "_").Replace("+", "-");

    public async ValueTask DisposeAsync()
    {
        if (_process is not { HasExited: false })
        {
            _process?.Dispose();
            _log?.Dispose();
            return;
        }

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
        _log?.Dispose();
    }
}
