using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

// ProductCategory DTOs
public record ProductCategoryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("icon")] string? Icon,  // Emoji glyph chosen in the UI
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("productCount")] int ProductCount  // number of products in this category
);

public record CreateProductCategoryRequest(
    [property: JsonPropertyName("name")][param: Required][param: StringLength(100, MinimumLength = 1)] string Name,
    [property: JsonPropertyName("description")][param: StringLength(500)] string? Description,
    [property: JsonPropertyName("icon")][param: StringLength(32)] string? Icon
);

public record UpdateProductCategoryRequest(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")][param: Required][param: StringLength(100, MinimumLength = 1)] string Name,
    [property: JsonPropertyName("description")][param: StringLength(500)] string? Description,
    [property: JsonPropertyName("icon")][param: StringLength(32)] string? Icon,
    [property: JsonPropertyName("isActive")] bool IsActive
);
