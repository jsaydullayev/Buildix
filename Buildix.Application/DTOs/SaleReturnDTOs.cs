using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>«Оформить возврат» — bitta sotuvdan bir necha liniya qaytarish.</summary>
public record CreateReturnDto(
    [property: JsonPropertyName("saleId")]
    [param: Required]
    Guid SaleId,

    // "Defect" | "NotFit" | "SellerError" | "Other"
    [property: JsonPropertyName("reason")]
    [param: Required]
    string Reason,

    // "Cash" | "Terminal" | "Transfer" — Наличные / На карту / Перечисление
    [property: JsonPropertyName("refundMethod")]
    [param: Required]
    string RefundMethod,

    [property: JsonPropertyName("comment")]
    [param: StringLength(500)]
    string? Comment,

    [property: JsonPropertyName("items")]
    [param: Required]
    [param: MinLength(1, ErrorMessage = "Kamida bitta tovar tanlang")]
    List<CreateReturnLineDto> Items
);

public record CreateReturnLineDto(
    [property: JsonPropertyName("saleItemId")] Guid SaleItemId,
    [property: JsonPropertyName("quantity")]
    [param: Range(0.001, double.MaxValue)]
    decimal Quantity
);

public record SaleReturnItemDto(
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice,
    [property: JsonPropertyName("lineTotal")] decimal LineTotal
);

public record SaleReturnDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("saleId")] Guid SaleId,
    [property: JsonPropertyName("saleNumber")] int SaleNumber,
    // "Defect" | "NotFit" | "SellerError" | "Other"
    [property: JsonPropertyName("reason")] string Reason,
    // "Cash" | "Terminal" | "Transfer"
    [property: JsonPropertyName("refundMethod")] string RefundMethod,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("userName")] string? UserName,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<SaleReturnItemDto> Items
);

/// <summary>Возвраты ekrani sarlavhasi — oylik jami + % выручки.</summary>
public record ReturnsSummaryDto(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("revenuePercent")] decimal RevenuePercent
);
