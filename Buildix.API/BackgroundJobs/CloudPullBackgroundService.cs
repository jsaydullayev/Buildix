using Buildix.Application.Interfaces;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Bulutdan o'zgarishlarni muntazam olib turadi: do'kon xodimlari va obuna
/// holati bulutga tegishli va do'kon ularni faqat shu yo'l bilan biladi.
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
public class CloudPullBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudPullBackgroundService> _logger;

    public CloudPullBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CloudPullBackgroundService> logger)
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

                var result = await sync.PullAsync(stoppingToken);
                if (!result.Success)
                {
                    // Xizmat sababni bazaga yozdi; bu yerda faqat jurnal.
                    // Istisno TASHLANMAYDI: aloqa uzilishi do'konda normal
                    // holat va u fon xizmatini o'ldirmasligi kerak.
                    _logger.LogWarning("Cloud pull unsuccessful: {Error}", result.Error);
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
