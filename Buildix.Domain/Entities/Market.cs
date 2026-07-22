namespace Buildix.Domain.Entities;

/// <summary>
/// Market/Tenant - alohida biznes egasi
/// </summary>
public class Market
{
    public int Id { get; set; }  // Primary Key - int (auto-increment)
    public string Name { get; set; } = string.Empty;
    public string? Subdomain { get; set; }  // market1.example.com
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }  // Subscription uchun
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Block state (operational, reversible) ────────────────────────────
    // Separate from IsActive (which is the soft-delete flag set by
    // DeleteOwner). A blocked market still exists and can be restored — it
    // simply rejects all authentication and tenant resolution attempts.
    // Typical use: subscription payment lapsed.
    public bool IsBlocked { get; set; } = false;
    public DateTime? BlockedAt { get; set; }
    public string? BlockedReason { get; set; }
    public Guid? BlockedByUserId { get; set; }

    // Owner who created this market
    public Guid OwnerId { get; set; }

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Zakup> Zakups { get; set; } = new List<Zakup>();
    public ICollection<Debt> Debts { get; set; } = new List<Debt>();
    public CashRegister? CashRegister { get; set; }

    // ── Subscription rule (single source of truth) ───────────────────────
    // The whole platform gates a tenant's "front door" (subdomain login +
    // real-time request access) on these two methods so the definition of
    // "open" never drifts between the login path, the middleware and the
    // public market-state endpoint. Time is always UTC (compare against
    // DateTime.UtcNow / ITashkentClock.UtcNow).

    /// <summary>
    /// Subscription has a set end date that has passed. Blocked/soft-deleted
    /// markets are handled by their own gates (IsBlocked → 423, !IsActive →
    /// not-found) so this returns false for them — an expired-but-blocked
    /// market surfaces as "blocked", not "expired". A null <see cref="ExpiresAt"/>
    /// means "no expiry set" (grandfathered / unlimited) and is never expired.
    /// </summary>
    public bool IsSubscriptionExpired(DateTime nowUtc) =>
        IsActive && !IsBlocked && ExpiresAt.HasValue && ExpiresAt.Value <= nowUtc;

    /// <summary>
    /// The market's login door is open: not blocked, not soft-deleted, and
    /// either no expiry is set or it is still in the future.
    /// </summary>
    public bool IsSubscriptionActive(DateTime nowUtc) =>
        IsActive && !IsBlocked && (!ExpiresAt.HasValue || ExpiresAt.Value > nowUtc);
}
