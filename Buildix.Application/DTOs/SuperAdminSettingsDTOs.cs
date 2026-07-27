using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>Tarif narxi va limitlari (Настройки → Тарифы).</summary>
public record SaPlanPriceDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("priceUzs")] decimal PriceUzs,
    [property: JsonPropertyName("maxUsers")] int MaxUsers,
    [property: JsonPropertyName("maxPoints")] int MaxPoints
);

public record SaSettingsDto(
    [property: JsonPropertyName("plans")] IReadOnlyList<SaPlanPriceDto> Plans,
    [property: JsonPropertyName("graceDays")] int GraceDays,
    [property: JsonPropertyName("warnOnOverdue")] bool WarnOnOverdue,
    [property: JsonPropertyName("restrictAfterGrace")] bool RestrictAfterGrace,
    [property: JsonPropertyName("fullBlockAfterDays")] int FullBlockAfterDays,
    [property: JsonPropertyName("soonThresholdDays")] int SoonThresholdDays,
    [property: JsonPropertyName("notifyExpiring")] bool NotifyExpiring,
    [property: JsonPropertyName("notifyBlocked")] bool NotifyBlocked,
    [property: JsonPropertyName("expiryReminderDays")] int ExpiryReminderDays,
    [property: JsonPropertyName("supportPhone")] string? SupportPhone,
    [property: JsonPropertyName("supportTelegram")] string? SupportTelegram,
    [property: JsonPropertyName("supportEmail")] string? SupportEmail
);

public record SaUpdateSettingsDto(
    [property: JsonPropertyName("plans")] IReadOnlyList<SaPlanPriceDto>? Plans,
    [property: JsonPropertyName("graceDays")] int GraceDays,
    [property: JsonPropertyName("warnOnOverdue")] bool WarnOnOverdue,
    [property: JsonPropertyName("restrictAfterGrace")] bool RestrictAfterGrace,
    [property: JsonPropertyName("fullBlockAfterDays")] int FullBlockAfterDays,
    [property: JsonPropertyName("soonThresholdDays")] int SoonThresholdDays,
    [property: JsonPropertyName("notifyExpiring")] bool NotifyExpiring,
    [property: JsonPropertyName("notifyBlocked")] bool NotifyBlocked,
    [property: JsonPropertyName("expiryReminderDays")] int ExpiryReminderDays,
    [property: JsonPropertyName("supportPhone")]
    [param: StringLength(30)]
    string? SupportPhone,
    [property: JsonPropertyName("supportTelegram")]
    [param: StringLength(100)]
    string? SupportTelegram,
    [property: JsonPropertyName("supportEmail")]
    [param: StringLength(150)]
    string? SupportEmail
)
{
    public SaUpdateSettingsDto() : this(null, 5, true, true, 30, 7, true, true, 3, null, null, null) { }
}

/// <summary>
/// Kirish sahifasi uchun ochiq kontaktlar. Anonim endpoint — shuning uchun
/// sozlamalarning FAQAT shu uch maydoni chiqadi.
/// </summary>
public record PublicSupportContactsDto(
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("telegram")] string? Telegram,
    [property: JsonPropertyName("email")] string? Email
);
