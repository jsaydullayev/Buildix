namespace Buildix.Application.Interfaces;

/// <summary>What a caller should do after trying to claim an idempotency key.</summary>
public enum IdempotencyDecision
{
    /// <summary>Key claimed — run the operation, then call <see cref="IIdempotencyService.CompleteAsync"/>.</summary>
    Proceed,
    /// <summary>Key already completed — return the stored response (don't re-run).</summary>
    Replay,
    /// <summary>Key claimed by an in-flight duplicate — tell the client to retry (409).</summary>
    InProgress,
    /// <summary>Same key, different request payload — reject (422).</summary>
    PayloadMismatch,
}

/// <summary>Outcome of <see cref="IIdempotencyService.BeginAsync"/>.</summary>
public sealed record IdempotencyBegin(IdempotencyDecision Decision, int StatusCode = 0, string? Body = null);

/// <summary>
/// Backing store for the <c>Idempotency-Key</c> contract on money-moving
/// endpoints. Opt-in: only engaged when the client sends the header. See
/// <c>Buildix.Infrastructure.Services.IdempotencyService</c>.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Atomically claim (scope, key) for this market. Proceed = we own it and
    /// must run the op; Replay/InProgress/PayloadMismatch short-circuit.
    /// </summary>
    Task<IdempotencyBegin> BeginAsync(
        string scope, string key, int marketId, string requestHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record the operation's outcome. A 2xx is persisted for replay; any other
    /// status releases the claim so a genuine retry can execute.
    /// </summary>
    Task CompleteAsync(
        string scope, string key, int marketId, int statusCode, string? body, CancellationToken cancellationToken = default);
}
