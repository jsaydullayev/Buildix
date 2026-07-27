using Buildix.Application.Interfaces;

namespace Buildix.Tests;

/// <summary>
/// Testlar uchun o'zgarmas platforma sozlamalari. Servis kesh bilan
/// ishlaganda test DB'ga bog'lanib qolmasin — qoidani test o'zi tanlaydi.
/// </summary>
public sealed class FixedPlatformSettings : IPlatformSettingsProvider
{
    public static IPlatformSettingsProvider Default => new FixedPlatformSettings(PlatformSettingsSnapshot.Defaults);

    public static IPlatformSettingsProvider With(int graceDays, int fullBlockAfterDays, bool restrictAfterGrace = true) =>
        new FixedPlatformSettings(PlatformSettingsSnapshot.Defaults with
        {
            GraceDays = graceDays,
            FullBlockAfterDays = fullBlockAfterDays,
            RestrictAfterGrace = restrictAfterGrace,
        });

    public static IPlatformSettingsProvider WithNotifications(bool expiring = true, bool blocked = true) =>
        new FixedPlatformSettings(PlatformSettingsSnapshot.Defaults with
        {
            NotifyExpiring = expiring,
            NotifyBlocked = blocked,
        });

    private FixedPlatformSettings(PlatformSettingsSnapshot snapshot) => Current = snapshot;

    public PlatformSettingsSnapshot Current { get; }

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
