using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Закуп ekranidagi «Рекомендуем заказать» qatori: sotuv tezligiga asoslangan
/// qayta-buyurtma tavsiyasi.
/// </summary>
public record ReorderSuggestionDto(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("name")] string Name,
    // UnitName — serverdagi o'zbekcha qisqartma ("dona", "kg"); ruscha yoki
    // inglizcha interfeysda noto'g'ri o'qiladi, shuning uchun raqamli UnitType
    // ham yuboriladi va klient nomni o'z tilida chiqaradi.
    [property: JsonPropertyName("unitName")] string UnitName,
    [property: JsonPropertyName("unit")] int Unit,
    [property: JsonPropertyName("currentQty")] decimal CurrentQty,
    [property: JsonPropertyName("minThreshold")] decimal MinThreshold,
    [property: JsonPropertyName("avgDailySales")] decimal AvgDailySales,
    // Joriy qoldiq necha kunga yetadi (avgDailySales > 0 bo'lsa). null = sotuv yo'q.
    [property: JsonPropertyName("daysOfCover")] int? DaysOfCover,
    [property: JsonPropertyName("suggestedQty")] decimal SuggestedQty
);
