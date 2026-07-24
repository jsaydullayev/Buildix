using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

public record NotificationDto(
    [property: JsonPropertyName("id")] Guid Id,
    // "Warehouse" | "Debt" | "Shift" | "Supply"
    [property: JsonPropertyName("category")] string Category,
    // "Info" | "Success" | "Warning" | "Danger"
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("actionTarget")] string? ActionTarget,
    [property: JsonPropertyName("isRead")] bool IsRead,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

public record NotificationFeedDto(
    [property: JsonPropertyName("unreadCount")] int UnreadCount,
    [property: JsonPropertyName("items")] IReadOnlyList<NotificationDto> Items
);
