using Microsoft.EntityFrameworkCore;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Common;

/// <summary>
/// Per-market sequential-number allocation guard for customer-facing document
/// numbers (ЧЕК № on a Sale, № on a ZakupReceipt). Takes a PostgreSQL
/// transaction-scoped advisory lock keyed on (entity-class, marketId) so two
/// concurrent allocators in the same market serialise on the lock and can never
/// compute the same MAX(number)+1. Replaces the old bare max+1, which had a
/// documented "rare concurrent-create race may reuse a number" — unacceptable
/// for a financial document.
///
/// Contract: call INSIDE an open transaction, immediately BEFORE the MAX(...)+1
/// read and the insert that consumes the number. The lock auto-releases on
/// commit/rollback and is scoped to a single market, so other markets never
/// block. No-op on the InMemory test provider (advisory locks are Postgres-only).
///
/// This makes a unique index unnecessary: with every allocation path holding the
/// lock, collisions cannot occur, so no data-migration (which could fail on any
/// pre-existing duplicate) is required.
/// </summary>
public static class MarketSequenceLock
{
    // Advisory-lock class ids — distinct namespaces so a Sale-number lock and a
    // ZakupReceipt-number lock for the same market never contend with each other.
    public const int SaleNumberClass = 1;
    public const int ZakupReceiptNumberClass = 2;

    public static async Task AcquireAsync(
        IAppDbContext context, int lockClass, int marketId, CancellationToken ct)
    {
        // pg_advisory_xact_lock(int4, int4): two-key form. The result set (a
        // single void row) is discarded — the lock is a side effect that lasts
        // until the surrounding transaction ends.
        if (context.Database.ProviderName?.Contains("InMemory") == false)
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockClass}, {marketId})", ct);
    }
}
