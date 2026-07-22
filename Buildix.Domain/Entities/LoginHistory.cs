using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

/// <summary>
/// A single login attempt for a known user (Account "Последние входы"). Distinct
/// from <see cref="LoginAttempt"/> (a per-username brute-force counter) and from
/// the audit log — this is the user-facing sign-in history. <c>CreatedAt</c>
/// (BaseEntity, UTC) is the moment of the attempt.
/// </summary>
public class LoginHistory : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>"Windows · Chrome" etc. — from the User-Agent.</summary>
    public string? DeviceInfo { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>True = successful login; false = wrong password for this account.</summary>
    public bool Success { get; set; }
}
