namespace Buildix.Domain.Exceptions;

/// <summary>
/// Thrown when a request would operate on a market the SuperAdmin has blocked
/// (typically non-payment). Maps to 423 Locked with code MARKET_BLOCKED so the
/// client renders a "contact admin" screen with the reason.
/// </summary>
public class MarketBlockedException : DomainException
{
    public int MarketId { get; }

    public override int StatusCode => 423;
    public override string Code => "MARKET_BLOCKED";
    public override string UserMessage => "Do'kon administrator tomonidan bloklangan. Iltimos, administrator bilan bog'laning.";
    public override string? Reason { get; }
    public override DateTime? BlockedAt { get; }

    public MarketBlockedException(int marketId, string? reason, DateTime? blockedAt)
        : base($"Market {marketId} is blocked: {reason}")
    {
        MarketId = marketId;
        Reason = reason;
        BlockedAt = blockedAt;
    }
}
