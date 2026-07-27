using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Buildix.Application.Validation;

namespace Buildix.Application.DTOs;

/// <summary>
/// «Пользователи платформы» ro'yxatining bir qatori — barcha do'konlar bo'ylab.
/// </summary>
public record SaUserRowDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("phone")] string? Phone,
    /// <summary>«Owner» | «Admin» | «Seller».</summary>
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("marketId")] int? MarketId,
    [property: JsonPropertyName("storeName")] string? StoreName,
    [property: JsonPropertyName("lastActiveAt")] DateTime? LastActiveAt,
    [property: JsonPropertyName("isActive")] bool IsActive
);

/// <summary>
/// Parolni tiklash. Parolni SuperAdmin O'ZI qo'yadi va foydalanuvchiga shaxsan
/// beradi — SMS orqali yuborilmaydi (TZ BE-S7 qarori).
/// </summary>
public record SaResetPasswordDto(
    [property: JsonPropertyName("newPassword")]
    [param: Required]
    [param: StrongPassword]
    string NewPassword
)
{
    public SaResetPasswordDto() : this(string.Empty) { }
}
