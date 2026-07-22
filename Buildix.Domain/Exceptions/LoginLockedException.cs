namespace Buildix.Domain.Exceptions;

/// <summary>
/// Thrown when a login is refused because the username accumulated too many
/// failed attempts in the recent window. Maps to 429 Too Many Requests with
/// code ACCOUNT_LOCKED so the client shows "try again in N minutes" rather than
/// mistaking the lock for a wrong password. Per-username (not per-IP) — it
/// catches an attacker rotating IPs to defeat the request-rate limiter.
/// </summary>
public class LoginLockedException : DomainException
{
    public string Username { get; }
    public DateTime LockedUntilUtc { get; }
    public int FailureCount { get; }

    public override int StatusCode => 429;
    public override string Code => "ACCOUNT_LOCKED";
    public override string UserMessage => "Hisob vaqtinchalik bloklandi. Bir necha daqiqadan keyin qaytadan urinib ko'ring.";
    public override DateTime? BlockedAt => LockedUntilUtc;

    public LoginLockedException(string username, DateTime lockedUntilUtc, int failureCount)
        : base($"Account '{username}' is locked until {lockedUntilUtc:O} after {failureCount} failed attempts.")
    {
        Username = username;
        LockedUntilUtc = lockedUntilUtc;
        FailureCount = failureCount;
    }
}
