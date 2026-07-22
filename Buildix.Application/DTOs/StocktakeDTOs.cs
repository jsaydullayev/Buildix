using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>Bitta mahsulotning faktik (sanab chiqilgan) qoldig'i.</summary>
public record StocktakeItem(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("countedQty")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Miqdor manfiy bo'lishi mumkin emas")]
    decimal CountedQty
);

/// <summary>Инвентаризация — faktik sanoq natijalari (bir nechta mahsulot).</summary>
public record StocktakeRequest(
    [property: JsonPropertyName("items")]
    [param: Required]
    List<StocktakeItem> Items
);

/// <summary>Bitta mahsulot bo'yicha tuzatish natijasi (variance).</summary>
public record StocktakeLineResult(
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("before")] decimal Before,
    [property: JsonPropertyName("counted")] decimal Counted,
    [property: JsonPropertyName("variance")] decimal Variance
);

public record StocktakeResultDto(
    [property: JsonPropertyName("adjustedCount")] int AdjustedCount,
    [property: JsonPropertyName("lines")] List<StocktakeLineResult> Lines
);
