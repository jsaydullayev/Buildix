using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// «Магазины» ro'yxatining bir qatori — do'kon markazida (egasi markazida
/// emas). Mavjud <see cref="OwnerSummaryDto"/> owner ro'yxati uchun qoladi:
/// u boshqa savolga javob beradi («kim egasi»), bu esa «do'kon qanday holatda».
/// </summary>
public record SaStoreRowDto(
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("ownerId")] Guid OwnerId,
    [property: JsonPropertyName("ownerName")] string OwnerName,
    [property: JsonPropertyName("ownerPhone")] string? OwnerPhone,
    /// <summary>Tarif nomi. <c>null</c> — model hali yo'q (BE-S1, S3 bosqichi).</summary>
    [property: JsonPropertyName("plan")] string? Plan,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    [property: JsonPropertyName("users")] int Users,
    /// <summary>«Active» | «Overdue» | «Blocked».</summary>
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isBlocked")] bool IsBlocked,
    [property: JsonPropertyName("lastActivityUtc")] DateTime? LastActivityUtc
);

/// <summary>Qator bosilganda ochiladigan detal panel (dizaynda drawer).</summary>
public record SaStoreDetailDto(
    [property: JsonPropertyName("store")] SaStoreRowDto Store,
    [property: JsonPropertyName("blockedAt")] DateTime? BlockedAt,
    [property: JsonPropertyName("blockedReason")] string? BlockedReason,
    [property: JsonPropertyName("stats")] SaStoreStatsDto Stats,
    /// <summary>
    /// To'lovlar tarixi. Hozircha HAR DOIM bo'sh: obuna to'lovlari jadvali
    /// S3 bosqichida qo'shiladi (BE-S2). Maydon shu yerda turadi, klient esa
    /// bo'sh holatni ko'rsatadi — S3'da faqat ma'lumot to'ladi, shakl emas.
    /// </summary>
    [property: JsonPropertyName("payments")] IReadOnlyList<SaStorePaymentDto> Payments
);

public record SaStoreStatsDto(
    [property: JsonPropertyName("users")] int Users,
    /// <summary>Shu oydagi yakunlangan cheklar (Toshkent kalendari).</summary>
    [property: JsonPropertyName("checksThisMonth")] int ChecksThisMonth,
    [property: JsonPropertyName("lastActivityUtc")] DateTime? LastActivityUtc,
    [property: JsonPropertyName("outstandingDebt")] decimal OutstandingDebt
);

public record SaStorePaymentDto(
    [property: JsonPropertyName("paidAtUtc")] DateTime PaidAtUtc,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("amountUzs")] decimal AmountUzs
);
