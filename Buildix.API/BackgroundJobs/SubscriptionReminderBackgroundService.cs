using Buildix.Application.Interfaces;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// Obuna tugashi haqida do'kon egalariga eslatma (Telegram).
///
/// <para>Kuniga bir marta emas, HAR SOAT o'tadi va ishni
/// <see cref="ISubscriptionNotifier.RemindExpiringAsync"/> ning o'zi
/// idempotent qiladi: stamp <c>Market.ExpiresAt</c> ning o'zi, ya'ni bir davr
/// uchun eslatma bir marta ketadi. Tez-tez o'tish serverni qayta ishga
/// tushirish yoki bir necha soatlik uzilish eslatmani butun kunga yo'qotib
/// yubormasligini kafolatlaydi.</para>
/// </summary>
public class SubscriptionReminderBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SubscriptionReminderBackgroundService> _logger;

    public SubscriptionReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SubscriptionReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Migratsiya va seed tugashini kutamiz.
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<ISubscriptionNotifier>();
                await notifier.RemindExpiringAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Bitta o'tishdagi xato tsiklni o'ldirmasin.
                _logger.LogError(ex, "Subscription reminder pass failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
