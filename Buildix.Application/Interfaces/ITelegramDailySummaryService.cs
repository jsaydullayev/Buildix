namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the market's day summary for the Telegram bot (sales, profit, cash,
/// debts, stock signals).
///
/// Deliberately takes <paramref name="marketId"/> explicitly instead of reading
/// <see cref="ICurrentMarketService"/>: it is also called from a background job
/// where there is no HttpContext, so the tenant query-filter degrades to a
/// no-op. Every query inside filters by MarketId by hand.
/// </summary>
public interface ITelegramDailySummaryService
{
    /// <summary>
    /// HTML-formatted summary (Telegram parse_mode=HTML) for the given Tashkent
    /// calendar day. Returns null when the market does not exist.
    /// </summary>
    Task<string?> BuildAsync(int marketId, DateTime localDate, CancellationToken cancellationToken = default);
}
