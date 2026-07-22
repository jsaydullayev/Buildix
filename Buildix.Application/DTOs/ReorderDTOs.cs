using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Закуп ekranidagi «Рекомендуем заказать» qatori: sotuv tezligiga asoslangan
/// qayta-buyurtma tavsiyasi.
/// </summary>
public record ReorderSuggestionDto(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("unitName")] string UnitName,
    [property: JsonPropertyName("currentQty")] decimal CurrentQty,
    [property: JsonPropertyName("minThreshold")] decimal MinThreshold,
    [property: JsonPropertyName("avgDailySales")] decimal AvgDailySales,
    // Joriy qoldiq necha kunga yetadi (avgDailySales > 0 bo'lsa). null = sotuv yo'q.
    [property: JsonPropertyName("daysOfCover")] int? DaysOfCover,
    [property: JsonPropertyName("suggestedQty")] decimal SuggestedQty
);
