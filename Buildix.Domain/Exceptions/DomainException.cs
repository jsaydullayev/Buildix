namespace Buildix.Domain.Exceptions;

/// <summary>
/// Base for expected domain-rule violations that the API translates to a
/// specific HTTP response. Each carries its own <see cref="StatusCode"/>,
/// machine-readable <see cref="Code"/> and client-safe <see cref="UserMessage"/>,
/// so the global exception handler maps every one of them with a single
/// <c>case DomainException</c> arm — no per-type switch, and the code/status/
/// message live on the exception that owns them rather than drifting in the
/// middleware. The base Exception message stays the internal (English)
/// diagnostic for logs; <see cref="UserMessage"/> is what the client sees.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>HTTP status the API should return (e.g. 409, 423, 429).</summary>
    public abstract int StatusCode { get; }

    /// <summary>Stable machine-readable code the client branches on (e.g. USERNAME_TAKEN).</summary>
    public abstract string Code { get; }

    /// <summary>Localized, client-safe message (never leaks internals).</summary>
    public abstract string UserMessage { get; }

    /// <summary>Optional timestamp surfaced to the client (lock/block time). Null when N/A.</summary>
    public virtual DateTime? BlockedAt => null;

    /// <summary>Optional subscription end date surfaced to the client (e.g. expired subscription). Null when N/A.</summary>
    public virtual DateTime? ExpiresAt => null;

    /// <summary>Optional human reason surfaced to the client (e.g. block reason). Null when N/A.</summary>
    public virtual string? Reason => null;

    protected DomainException(string internalMessage) : base(internalMessage) { }
}
