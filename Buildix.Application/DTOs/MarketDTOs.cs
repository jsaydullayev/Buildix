using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Buildix.Application.Validation;

namespace Buildix.Application.DTOs;

public record CreateMarketRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("adminFullName")] string AdminFullName,
    [property: JsonPropertyName("adminUsername")] string AdminUsername,
    [property: JsonPropertyName("adminPassword")][param: Required][param: StrongPassword] string AdminPassword,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt = null
);

public record MarketDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

/// <summary>
/// Public, pre-auth view of a market's login "door", keyed by slug. Drives the
/// buildix.uz/{subdomain}/login screen: <c>State</c> = "active" (show the login
/// form), "expired" (subscription lapsed — show renew notice) or "blocked"
/// (admin-blocked — show contact-admin notice). Carries NO user or business
/// data — only what the login gate needs.
/// </summary>
public record PublicMarketStateDto(
    [property: JsonPropertyName("subdomain")] string Subdomain,
    [property: JsonPropertyName("marketName")] string MarketName,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt
);

// Owner market registration DTOs
public record RegisterMarketRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt = null
);

public record RegisterMarketResponse(
    [property: JsonPropertyName("market")] MarketDto Market,
    [property: JsonPropertyName("owner")] UserDto Owner,
    [property: JsonPropertyName("accessToken")] string AccessToken
);

public record UpdateMyMarketRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description
);

public record UpdateMarketRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description
);
