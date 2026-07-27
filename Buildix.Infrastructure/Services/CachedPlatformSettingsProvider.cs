using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buildix.Infrastructure.Services;

/// <summary>
/// Platforma sozlamalarining xotiradagi nusxasi (<see cref="IPlatformSettingsProvider"/>).
///
/// <para><b>Nega kesh.</b> Obuna eshigi HAR so'rovda tekshiriladi
/// (TenantResolutionMiddleware). Sozlamalarni har safar DB'dan o'qish butun
/// platformaga bitta qator uchun qo'shimcha so'rov qo'shardi.</para>
///
/// <para><b>Nega fail-open.</b> Kesh hali to'lmagan bo'lsa (startup xatosi)
/// <see cref="PlatformSettingsSnapshot.Defaults"/> ishlatiladi. Bu ataylab:
/// sozlamani o'qiy olmaganimiz uchun ishlab turgan do'konlarni yopib qo'yish
/// zarari — ularni ochiq qoldirishdan kattaroq. Blok — operatorning ataylab
/// qilgan amali, u <c>Market.IsBlocked</c> da saqlanadi va keshga bog'liq emas.</para>
/// </summary>
public sealed class CachedPlatformSettingsProvider : IPlatformSettingsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CachedPlatformSettingsProvider> _logger;
    private volatile PlatformSettingsSnapshot _current = PlatformSettingsSnapshot.Defaults;

    public CachedPlatformSettingsProvider(
        IServiceScopeFactory scopeFactory,
        ILogger<CachedPlatformSettingsProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public PlatformSettingsSnapshot Current => _current;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (row is null)
            {
                _logger.LogWarning("PlatformSettings row is missing — using defaults.");
                _current = PlatformSettingsSnapshot.Defaults;
                return;
            }

            _current = ToSnapshot(row);
        }
        catch (Exception ex)
        {
            // Fail-open (yuqoridagi izohga qarang) — lekin jimgina emas.
            _logger.LogError(ex, "PlatformSettings reload failed — keeping the previous snapshot.");
        }
    }

    private static PlatformSettingsSnapshot ToSnapshot(PlatformSettings s) => new(
        s.GraceDays, s.WarnOnOverdue, s.RestrictAfterGrace, s.FullBlockAfterDays,
        s.SoonThresholdDays, s.NotifyExpiring, s.NotifyBlocked, s.ExpiryReminderDays,
        s.SupportPhone, s.SupportTelegram, s.SupportEmail);
}
