using Buildix.Application.Interfaces;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Bulut bilan ikki tomonlama sinxronizatsiya: avval o'zgarishlarni oladi,
/// so'ng o'zining savdolarini yuboradi.
///
/// <para><b>Birinchi tortish DARHOL.</b> Yangi o'rnatilgan do'kon bazasi
/// bo'sh — na market, na foydalanuvchi bor, ya'ni kirish oynasidan nariga
/// o'tib bo'lmaydi. Birinchi tortishni kutish ilovani ochgan odamni
/// «nega kirolmayapman?» degan savol bilan qoldirardi.</para>
///
/// <para><b>Keyin har besh daqiqada.</b> Egasi telefondan xodim qo'shsa, u
/// do'konda taxminan shuncha vaqtdan keyin paydo bo'ladi — bu kutishga
/// arziydigan oraliq va u internetsiz do'konda ham keraksiz yuk
/// bermaydi.</para>
///
/// <para><b>Bulut sozlanmagan bo'lsa umuman ishlamaydi.</b> Do'kon hali
/// bog'lanmagan bo'lishi mumkin va bu xato emas — u shunchaki lokal
/// ishlaydi.</para>
/// </summary>
public class CloudSyncBackgroundService : BackgroundService
{
    /// <summary>
    /// Sinxronizatsiya oralig'i.
    ///
    /// <para><b>Nega bir daqiqa.</b> Egasining telefonidagi raqam qanchalik
    /// eskirishi AYNAN shu songa bog'liq: interfeys allaqachon har 60
    /// soniyada yangilanadi, ya'ni kechikishning sababi u emas. Besh
    /// daqiqada egasi endigina bo'lgan savdoni olti daqiqagacha ko'rmasligi
    /// mumkin edi.</para>
    ///
    /// <para>Narxi arzimas: o'zgarish bo'lmasa so'rov bir necha yuz bayt,
    /// kuniga ~1,5 MB. Buning evaziga «jonli» so'zi rost bo'ladi.</para>
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudSyncBackgroundService> _logger;

    public CloudSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CloudSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<IShopSyncService>();

                if (!sync.IsConfigured) return;   // bog'lanmagan — qayta urinishning ma'nosi yo'q

                var pulled = await sync.PullAsync(stoppingToken);
                if (!pulled.Success)
                {
                    // Xizmat sababni bazaga yozdi; bu yerda faqat jurnal.
                    // Istisno TASHLANMAYDI: aloqa uzilishi do'konda normal
                    // holat va u fon xizmatini o'ldirmasligi kerak.
                    _logger.LogWarning("Cloud pull unsuccessful: {Error}", pulled.Error);
                }

                // ── Birinchi to'liq nusxa ─────────────────────────────────
                // Tortishdan KEYIN: do'kon o'z raqamini va xodimlarini faqat
                // o'sha yerdan biladi, ularsiz kelgan savdolarni bog'lab
                // bo'lmaydi.
                //
                // Aynan bir marta bajariladi. Usiz webda ishlab kelgan do'kon
                // desktopga o'tganda BO'SH ekran ko'rardi: savdo ham, tovar
                // ham yo'q — ular bulutda qolib ketardi va pastga tushadigan
                // yo'l yo'q edi.
                var seed = await sync.SeedAsync(stoppingToken);
                if (!seed.Success)
                    _logger.LogWarning("Cloud seed unsuccessful: {Error}", seed.Error);
                else if (seed.Rows > 0)
                    _logger.LogInformation(
                        "Cloud seed: {Rows} qator, tugadi={Completed}", seed.Rows, seed.Completed);

                // Yuborish TORTISHDAN KEYIN. Do'kon o'z market raqamini
                // faqat tortishdan biladi va usiz nima yuborishni ham
                // aniqlay olmaydi.
                //
                // Nusxa TUGAMAGUNCHA yubormaymiz: yarim to'ldirilgan do'kon
                // bulutga o'zining chala holatini qaytarib, u yerdagi to'g'ri
                // ma'lumotni bosib yuborishi mumkin edi.
                //
                // `continue` ISHLATILMAYDI: u quyidagi kutishni ham o'tkazib
                // yuborardi va tsikl bulutni to'xtovsiz so'rovga ko'mib
                // tashlardi.
                if (seed.Completed)
                {
                    var push = scope.ServiceProvider.GetRequiredService<IShopPushService>();
                    var pushed = await push.PushAsync(stoppingToken);
                    if (!pushed.Success)
                        _logger.LogWarning("Cloud push unsuccessful: {Error}", pushed.Error);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Kutilmagan xato ham tsiklni to'xtatmasligi kerak: bir marta
                // yiqilgan fon xizmati QAYTA ISHGA TUSHMAYDI va do'kon
                // shundan keyin bulutdan hech narsa olmasdi.
                _logger.LogError(ex, "Cloud pull threw unexpectedly");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
