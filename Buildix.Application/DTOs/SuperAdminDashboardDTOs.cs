using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// «Панель Buildix» — bitta so'rovda butun ekran.
///
/// <para>Nega bitta endpoint: dizaynda 4 KPI + 3 panel bor, ularni alohida
/// chaqirsak bitta sahifa ochilishi 7 ta so'rov bo'lardi va raqamlar bir-biriga
/// mos kelmasligi mumkin edi (oraliqda ariza qabul qilinsa, KPI bir holatni,
/// ro'yxat boshqasini ko'rsatardi). Bu yerda hammasi bitta snapshot.</para>
/// </summary>
public record SaDashboardDto(
    [property: JsonPropertyName("kpis")] SaDashboardKpisDto Kpis,
    [property: JsonPropertyName("newRequests")] IReadOnlyList<SaDashboardRequestDto> NewRequests,
    [property: JsonPropertyName("overdue")] IReadOnlyList<SaDashboardStoreDto> Overdue,
    [property: JsonPropertyName("expiringSoon")] IReadOnlyList<SaDashboardStoreDto> ExpiringSoon,
    [property: JsonPropertyName("stores")] IReadOnlyList<SaDashboardStoreDto> Stores
);

public record SaDashboardKpisDto(
    [property: JsonPropertyName("activeStores")] int ActiveStores,
    /// <summary>Shu oyda ochilgan do'konlar — dizayndagi «+2 за июль».</summary>
    [property: JsonPropertyName("newStoresThisMonth")] int NewStoresThisMonth,
    [property: JsonPropertyName("newRequests")] int NewRequests,
    /// <summary>
    /// Oylik obuna daromadi (MRR). <c>null</c> — tarif modeli hali yo'q (BE-S1,
    /// S3 bosqichi). Ataylab null: nol ko'rsatilsa, «daromad yo'q» degan
    /// yolg'on ma'lumot bo'lardi.
    /// </summary>
    [property: JsonPropertyName("monthlyRevenueUzs")] decimal? MonthlyRevenueUzs,
    [property: JsonPropertyName("overdueStores")] int OverdueStores
);

public record SaDashboardRequestDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

public record SaDashboardStoreDto(
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    [property: JsonPropertyName("users")] int Users,
    /// <summary>«Активен» | «Просрочка» | «Заблокирован» — klient shu bo'yicha bo'yaydi.</summary>
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isBlocked")] bool IsBlocked,
    [property: JsonPropertyName("lastActivityUtc")] DateTime? LastActivityUtc
);
