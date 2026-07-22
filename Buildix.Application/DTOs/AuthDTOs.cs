using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Buildix.Application.Validation;

namespace Buildix.Application.DTOs;

public record LoginRequest(
    [property: JsonPropertyName("username")]
    [param: Required(ErrorMessage = "Username majburiy")]
    [param: StringLength(50, MinimumLength = 3, ErrorMessage = "Username 3-50 belgi bo'lishi kerak")]
    string Username,

    // Login DOES NOT enforce the strong-password rule — old accounts created
    // under the previous 6-char policy must still be able to log in. The
    // policy applies only to PASSWORD CREATION (Register / CreateUser /
    // UpdateUser / UpdateProfile).
    [property: JsonPropertyName("password")]
    [param: Required(ErrorMessage = "Parol majburiy")]
    [param: StringLength(100, MinimumLength = 1, ErrorMessage = "Parol majburiy")]
    string Password,

    // The market slug (sub-path) the user is logging in through, taken from the
    // URL (buildix.uz/{subdomain}/login). OPTIONAL: when present it scopes the
    // login to that one market (disambiguates cross-tenant username collisions)
    // and gates on the market's subscription state; when absent the login stays
    // market-agnostic (SuperAdmin logs in from the root with no slug).
    [property: JsonPropertyName("subdomain")]
    string? Subdomain = null
) {
    public LoginRequest() : this(string.Empty, string.Empty) { }
}

// RegisterRequest removed alongside the public self-registration endpoint —
// onboarding is SuperAdmin-gated (see RegistrationRequestDTOs / SuperAdmin flow).

/// <summary>Account "Устройства и сессии" qatori — bitta faol sessiya.</summary>
public record SessionDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("lastUsedAt")] DateTime? LastUsedAt,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("isCurrent")] bool IsCurrent
);

/// <summary>«Завершить все другие сессии» — joriy refresh token yuboriladi.</summary>
public record RevokeOtherSessionsRequest(
    [property: JsonPropertyName("refreshToken")] string RefreshToken
);

/// <summary>Account "Последние входы" — bitta kirish urinishi.</summary>
public record LoginHistoryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("ipAddress")] string? IpAddress,
    [property: JsonPropertyName("atUtc")] DateTime AtUtc,
    [property: JsonPropertyName("success")] bool Success
);

public record RefreshTokenRequest(
    [property: JsonPropertyName("accessToken")]
    [param: Required(ErrorMessage = "Access token majburiy")]
    string AccessToken,

    [property: JsonPropertyName("refreshToken")]
    [param: Required(ErrorMessage = "Refresh token majburiy")]
    string RefreshToken
) {
    public RefreshTokenRequest() : this(string.Empty, string.Empty) { }
}

public record AuthResponse(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresAt")] DateTime ExpiresAt,
    // Owner RBAC — the user's effective permission set, so the client can
    // gate its UI. Owner/SuperAdmin receive the full catalogue.
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,

    // The tenant this session belongs to. The client stores these so that when
    // the user re-opens buildix.uz/{subdomain}/ it can match the saved session
    // to the path slug and skip login (silent auto-enter) instead of showing a
    // login form for the wrong market. Null for SuperAdmin (no market).
    [property: JsonPropertyName("marketId")] int? MarketId = null,
    [property: JsonPropertyName("subdomain")] string? Subdomain = null
) {
    public AuthResponse() : this(Guid.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, DateTime.MinValue, new List<string>()) { }
}
