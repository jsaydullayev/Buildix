namespace Buildix.Domain.Exceptions;

/// <summary>
/// Thrown when a request or login targets a market whose subscription has
/// lapsed (<c>ExpiresAt &lt;= now</c>). Maps to 402 Payment Required with code
/// SUBSCRIPTION_EXPIRED so the client renders an "obuna tugadi — yangilang"
/// screen and (for the whole subdomain) blocks entry until an admin extends
/// the subscription. Distinct from MARKET_BLOCKED (manual, 423) so the client
/// can show a renewal message rather than a "contact admin — blocked" one.
/// </summary>
public class SubscriptionExpiredException : DomainException
{
    public int MarketId { get; }

    public override int StatusCode => 402;
    public override string Code => "SUBSCRIPTION_EXPIRED";
    public override string UserMessage =>
        "Obuna muddati tugagan. Iltimos, administrator bilan bog'lanib obunani yangilang.";
    public override DateTime? ExpiresAt { get; }

    public SubscriptionExpiredException(int marketId, DateTime? expiresAt)
        : base($"Market {marketId} subscription expired at {expiresAt:O}")
    {
        MarketId = marketId;
        ExpiresAt = expiresAt;
    }
}
