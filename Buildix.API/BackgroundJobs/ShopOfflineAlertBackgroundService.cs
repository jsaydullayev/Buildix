using Buildix.Application.Interfaces;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Soatiga bir marta aloqaga chiqmayotgan do'konlarni tekshiradi.
///
/// <para>Qaror mantig'i xizmatda (<c>ShopOfflineAlertService</c>) — bu yerda
/// faqat jadval. Sababi oddiy: «keraksiz vahima yubormaslik» qoidalari
/// sinaladigan bo'lishi kerak, fon xizmatining ichini esa sinab
/// bo'lmaydi.</para>
/// </summary>
public class ShopOfflineAlertBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShopOfflineAlertBackgroundService> _logger;

    public ShopOfflineAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShopOfflineAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ishga tushishda darhol emas: API endigina ko'tarildi va do'konlar
        // hali aloqaga chiqishga ulgurmagan bo'lishi mumkin.
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<IShopOfflineAlertService>();
                await alerts.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Bir marta yiqilgan fon xizmati QAYTA ISHGA TUSHMAYDI —
                // shundan keyin hech qanday xabar kelmasdi.
                _logger.LogError(ex, "Shop offline check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
