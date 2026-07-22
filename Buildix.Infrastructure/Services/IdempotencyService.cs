using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Buildix.Infrastructure.Services;

/// <summary>
/// Idempotency-key store for money-moving endpoints. The claim is a plain INSERT
/// against a unique (MarketId, Scope, Key) index, so concurrency is resolved by
/// PostgreSQL itself: exactly one concurrent duplicate wins the INSERT and runs;
/// the rest hit the 23505 unique violation and are routed to replay / in-flight.
/// Lives in Infrastructure because detecting the unique violation needs the
/// Npgsql provider type (Application stays provider-agnostic).
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IAppDbContext _context;
    private readonly ILogger<IdempotencyService> _logger;

    // An in-flight claim older than this is treated as abandoned (the original
    // request crashed between running the op and recording its result) and can
    // be re-claimed, so a key is never permanently stuck. Money ops complete in
    // well under a second, so 2 minutes is comfortably conservative.
    private const int PendingStaleSeconds = 120;

    public IdempotencyService(IAppDbContext context, ILogger<IdempotencyService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IdempotencyBegin> BeginAsync(
        string scope, string key, int marketId, string requestHash, CancellationToken cancellationToken = default)
    {
        var record = new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Scope = scope,
            Key = key,
            RequestHash = requestHash,
            StatusCode = 0,
            CreatedAt = DateTime.UtcNow,
        };
        _context.IdempotencyRecords.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return new IdempotencyBegin(IdempotencyDecision.Proceed);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Someone already claimed this key. Drop our failed insert and read
            // the existing row to decide replay vs in-progress vs mismatch.
            _context.Entry(record).State = Microsoft.EntityFrameworkCore.EntityState.Detached;

            var existing = await _context.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.MarketId == marketId && r.Scope == scope && r.Key == key, cancellationToken);

            // Vanished between the INSERT and the read (pruned) — safest is to
            // tell the client to retry rather than double-execute.
            if (existing is null)
                return new IdempotencyBegin(IdempotencyDecision.InProgress);

            if (existing.StatusCode == 0)
                return await HandlePendingAsync(existing, requestHash, cancellationToken);

            if (!string.IsNullOrEmpty(requestHash)
                && !string.IsNullOrEmpty(existing.RequestHash)
                && existing.RequestHash != requestHash)
                return new IdempotencyBegin(IdempotencyDecision.PayloadMismatch);

            return new IdempotencyBegin(IdempotencyDecision.Replay, existing.StatusCode, existing.ResponseBody);
        }
    }

    /// <summary>
    /// An in-flight claim: retry (409) if it's recent, or atomically re-claim it
    /// if it's older than the stale window (the original op almost certainly
    /// crashed). The guarded UPDATE (…AND StatusCode=0 AND CreatedAt&lt;cutoff)
    /// means only one caller can win the re-claim.
    /// </summary>
    private async Task<IdempotencyBegin> HandlePendingAsync(
        IdempotencyRecord existing, string requestHash, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-PendingStaleSeconds);
        if (existing.CreatedAt >= cutoff)
            return new IdempotencyBegin(IdempotencyDecision.InProgress);

        var reclaimed = await _context.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""IdempotencyRecords""
               SET ""CreatedAt"" = {DateTime.UtcNow}, ""RequestHash"" = {requestHash}
             WHERE ""Id"" = {existing.Id} AND ""StatusCode"" = 0 AND ""CreatedAt"" < {cutoff}",
            cancellationToken);

        if (reclaimed == 1)
        {
            _logger.LogWarning(
                "Idempotency: re-claimed abandoned key scope={Scope} key={Key} (stale > {Sec}s)",
                existing.Scope, existing.Key, PendingStaleSeconds);
            return new IdempotencyBegin(IdempotencyDecision.Proceed);
        }
        return new IdempotencyBegin(IdempotencyDecision.InProgress);
    }

    public async Task CompleteAsync(
        string scope, string key, int marketId, int statusCode, string? body, CancellationToken cancellationToken = default)
    {
        var existing = await _context.IdempotencyRecords
            .FirstOrDefaultAsync(
                r => r.MarketId == marketId && r.Scope == scope && r.Key == key, cancellationToken);
        if (existing is null)
            return;

        if (statusCode is >= 200 and < 300)
        {
            existing.StatusCode = statusCode;
            existing.ResponseBody = body;
            existing.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            // Non-2xx (business failure / server error): release the claim so a
            // legitimate retry can run. We never permanently persist a failure.
            _context.IdempotencyRecords.Remove(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
