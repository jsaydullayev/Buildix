namespace Buildix.Domain.Entities;

/// <summary>
/// One row per (market, scope, idempotency-key). Lets a money-moving endpoint
/// de-duplicate a retried request (double-click, mobile retry, proxy replay):
/// the first request INSERTs a pending row and runs; a duplicate finds the row
/// and either replays the stored 2xx response, is told to retry (the first is
/// still in flight), or is rejected (same key, different payload). Only 2xx
/// outcomes are persisted — a failed op removes its claim so a genuine retry can
/// execute. Rows are safe to prune after a retention window.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; }

    /// <summary>Tenant scope — an Idempotency-Key is unique per market.</summary>
    public int MarketId { get; set; }

    /// <summary>
    /// Operation scope (e.g. "sale-payment") so the same key sent to two
    /// different endpoints never collides.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Client-supplied <c>Idempotency-Key</c> header value.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the bound request payload — detects a key reused with a
    /// DIFFERENT body (client bug) so the server can 422 instead of silently
    /// replaying the first call's response.
    /// </summary>
    public string? RequestHash { get; set; }

    /// <summary>Captured HTTP status. 0 = in flight (claimed, not yet completed).</summary>
    public int StatusCode { get; set; }

    /// <summary>Captured JSON response body — persisted only for a completed 2xx.</summary>
    public string? ResponseBody { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
