namespace Buildix.Application.Interfaces;

/// <summary>
/// Platforma sozlamalarining o'zgarmas nusxasi. Entity emas: bu qiymat HAR
/// so'rovda (middleware'da) o'qiladi, ya'ni u yerga EF obyektini olib kirish
/// tasodifiy DB chaqiruvlariga yo'l ochardi.
/// </summary>
public record PlatformSettingsSnapshot(
    int GraceDays,
    bool WarnOnOverdue,
    bool RestrictAfterGrace,
    int FullBlockAfterDays,
    int SoonThresholdDays,
    bool NotifyExpiring,
    bool NotifyBlocked,
    int ExpiryReminderDays,
    string? SupportPhone,
    string? SupportTelegram,
    string? SupportEmail)
{
    /// <summary>
    /// Sozlamalar hali o'qilmagan holat uchun xavfsiz qiymatlar. Ular
    /// ATAYLAB yumshoq: kesh bo'sh bo'lsa do'konlar yopilib qolgandan ko'ra
    /// ochiq qolgani ma'qul (blok — operatorning ataylab qilgan amali).
    /// </summary>
    public static readonly PlatformSettingsSnapshot Defaults =
        new(GraceDays: 5, WarnOnOverdue: true, RestrictAfterGrace: true,
            FullBlockAfterDays: 30, SoonThresholdDays: 7,
            NotifyExpiring: true, NotifyBlocked: true, ExpiryReminderDays: 3,
            SupportPhone: null, SupportTelegram: null, SupportEmail: null);
}

/// <summary>
/// Sozlamalarni keshdan beradi. <see cref="Current"/> — DB'ga tegmaydigan
/// O(1) o'qish (obuna eshigi har so'rovda tekshiriladi); yozuvdan keyin
/// <see cref="ReloadAsync"/> keshni yangilaydi.
/// </summary>
public interface IPlatformSettingsProvider
{
    PlatformSettingsSnapshot Current { get; }

    Task ReloadAsync(CancellationToken ct = default);
}
