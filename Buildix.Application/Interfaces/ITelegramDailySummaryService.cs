namespace Buildix.Application.Interfaces;

/// <summary>
/// What the reader is allowed to see. The bot serves every employee now, so the
/// summary can no longer assume an owner: each block is opt-in and mirrors the
/// permission the web panel gates the same figure on.
/// </summary>
/// <param name="SellerId">Non-null limits the sales figures to that seller's own
/// receipts — for a cashier without <c>data.allSalesView</c>, who must not see
/// the shop's total takings.</param>
/// <param name="IncludeProfit">data.profit — profit + margin lines.</param>
/// <param name="IncludeCash">data.cashBalance — the till balance.</param>
/// <param name="IncludeDebts">debts.access — customer debt totals.</param>
/// <param name="IncludeStock">products.access — low-stock signals.</param>
public sealed record DailySummaryOptions(
    Guid? SellerId = null,
    bool IncludeProfit = false,
    bool IncludeCash = false,
    bool IncludeDebts = false,
    bool IncludeStock = false)
{
    /// <summary>Everything, whole market — the owner's nightly push.</summary>
    public static readonly DailySummaryOptions Owner = new(null, true, true, true, true);
}

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
    /// calendar day, showing only what <paramref name="options"/> allows.
    /// Returns null when the market does not exist.
    /// </summary>
    Task<string?> BuildAsync(int marketId, DateTime localDate, DailySummaryOptions options,
        CancellationToken cancellationToken = default);
}
