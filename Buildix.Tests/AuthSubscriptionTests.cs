using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Covers the sub-path login + subscription redesign:
///   • the single-source subscription rule on <see cref="Market"/>,
///   • the public market-state endpoint (MarketService.GetPublicStateBySubdomainAsync),
///   • slug-scoped login: tenant disambiguation + block/expiry gating,
///   • real-time-independent login enforcement of expiry for the no-slug path.
/// Runs against the real AppDbContext (InMemory) with a real JwtService so the
/// happy path actually mints a token.
/// </summary>
public class AuthSubscriptionTests
{
    private const string Password = "Str0ng!Pass1";

    // ── wiring helpers ───────────────────────────────────────────────────

    private static IConfiguration BuildJwtConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-that-is-comfortably-long-1234567890",
                ["Jwt:Issuer"] = "BuildixAPI",
                ["Jwt:Audience"] = "BuildixClient",
                ["Jwt:AccessTokenExpireMinutes"] = "30",
                ["Jwt:RefreshTokenExpireDays"] = "7",
            })
            .Build();

    private static AuthService NewAuthService(TestHarness h)
    {
        var config = BuildJwtConfig();
        var jwt = new JwtService(config, NullLogger<JwtService>.Instance);
        return new AuthService(
            h.UnitOfWork,
            jwt,
            NullLogger<AuthService>.Instance,
            h.Db,
            config,
            h.Market,
            Substitute.For<IRevokedTokenStore>(),
            h.Audit,
            new InMemoryLoginAttemptTracker(),
            Substitute.For<IHttpContextAccessor>());
    }

    private static MarketService NewMarketService(TestHarness h) =>
        new(h.UnitOfWork, NullLogger<MarketService>.Instance, h.Db, Substitute.For<IJwtService>());

    /// <summary>Persist a market (Id auto-generated) and return it.</summary>
    private static async Task<Market> AddMarketAsync(
        TestHarness h, string subdomain,
        bool isActive = true, bool isBlocked = false, DateTime? expiresAt = null)
    {
        var market = new Market
        {
            Name = $"Market {subdomain}",
            Subdomain = subdomain,
            IsActive = isActive,
            IsBlocked = isBlocked,
            ExpiresAt = expiresAt,
            OwnerId = Guid.NewGuid(),
        };
        h.Db.Markets.Add(market);
        await h.Db.SaveChangesAsync();
        return market;
    }

    private static async Task<User> AddOwnerAsync(
        TestHarness h, int? marketId, string username,
        string password = Password, Role role = Role.Owner)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Owner " + username,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            IsActive = true,
            MarketId = marketId,
        };
        h.Db.Users.Add(user);
        await h.Db.SaveChangesAsync();
        return user;
    }

    // ── domain rule (single source of truth) ─────────────────────────────

    [Fact]
    public void Subscription_rule_matrix()
    {
        var now = DateTime.UtcNow;
        Market M(bool active, bool blocked, DateTime? exp) =>
            new() { IsActive = active, IsBlocked = blocked, ExpiresAt = exp };

        // Active: future expiry, or no expiry (grandfathered).
        Assert.True(M(true, false, now.AddDays(1)).IsSubscriptionActive(now));
        Assert.True(M(true, false, null).IsSubscriptionActive(now));
        Assert.False(M(true, false, now.AddDays(1)).IsSubscriptionExpired(now));
        Assert.False(M(true, false, null).IsSubscriptionExpired(now));

        // Expired: past expiry on an otherwise-open market.
        Assert.True(M(true, false, now.AddSeconds(-1)).IsSubscriptionExpired(now));
        Assert.False(M(true, false, now.AddSeconds(-1)).IsSubscriptionActive(now));

        // Blocked / inactive are never "active" and never "expired" (their own gates win).
        Assert.False(M(true, true, now.AddSeconds(-1)).IsSubscriptionExpired(now));
        Assert.False(M(true, true, now.AddDays(1)).IsSubscriptionActive(now));
        Assert.False(M(false, false, now.AddDays(1)).IsSubscriptionActive(now));
        Assert.False(M(false, false, now.AddSeconds(-1)).IsSubscriptionExpired(now));
    }

    // ── public market-state endpoint ─────────────────────────────────────

    [Fact]
    public async Task Public_state_reports_active_expired_blocked_and_404()
    {
        using var h = new TestHarness(marketId: null);
        await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddMarketAsync(h, "beta", expiresAt: DateTime.UtcNow.AddSeconds(-1));
        await AddMarketAsync(h, "gamma", isBlocked: true);
        await AddMarketAsync(h, "delta", isActive: false);
        h.Db.ChangeTracker.Clear();
        var svc = NewMarketService(h);

        Assert.Equal("active", (await svc.GetPublicStateBySubdomainAsync("alpha"))!.State);
        Assert.Equal("expired", (await svc.GetPublicStateBySubdomainAsync("beta"))!.State);
        Assert.Equal("blocked", (await svc.GetPublicStateBySubdomainAsync("gamma"))!.State);
        // Soft-deleted and unknown slugs both resolve to null → the controller 404s.
        Assert.Null(await svc.GetPublicStateBySubdomainAsync("delta"));
        Assert.Null(await svc.GetPublicStateBySubdomainAsync("nope"));
    }

    [Fact]
    public async Task Public_state_is_case_insensitive_on_slug()
    {
        using var h = new TestHarness(marketId: null);
        await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        h.Db.ChangeTracker.Clear();

        var state = await NewMarketService(h).GetPublicStateBySubdomainAsync("  ALPHA ");
        Assert.NotNull(state);
        Assert.Equal("alpha", state!.Subdomain);
        Assert.Equal("active", state.State);
    }

    // ── slug-scoped login ────────────────────────────────────────────────

    [Fact]
    public async Task Login_with_slug_succeeds_and_returns_market_identity()
    {
        using var h = new TestHarness(marketId: null);
        var market = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        var res = await NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password, "alpha"));

        Assert.NotNull(res);
        Assert.Equal(market.Id, res!.MarketId);
        Assert.Equal("alpha", res.Subdomain);
        Assert.False(string.IsNullOrEmpty(res.AccessToken));
    }

    [Fact]
    public async Task Login_slug_disambiguates_same_username_across_tenants()
    {
        using var h = new TestHarness(marketId: null);
        var a = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        var b = await AddMarketAsync(h, "beta", expiresAt: DateTime.UtcNow.AddDays(30));
        // Same username + same password in both markets — only the slug tells them apart.
        await AddOwnerAsync(h, a.Id, "sardor");
        await AddOwnerAsync(h, b.Id, "sardor");
        h.Db.ChangeTracker.Clear();
        var auth = NewAuthService(h);

        var viaAlpha = await auth.LoginAsync(new LoginRequest("sardor", Password, "alpha"));
        var viaBeta = await auth.LoginAsync(new LoginRequest("sardor", Password, "beta"));

        Assert.Equal(a.Id, viaAlpha!.MarketId);
        Assert.Equal(b.Id, viaBeta!.MarketId);
    }

    [Fact]
    public async Task Login_via_wrong_market_slug_fails()
    {
        using var h = new TestHarness(marketId: null);
        var a = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddMarketAsync(h, "beta", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddOwnerAsync(h, a.Id, "sardor"); // only exists in alpha
        h.Db.ChangeTracker.Clear();

        // Correct credentials, but through beta's door → scoped out → 401 (null).
        var res = await NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password, "beta"));

        Assert.Null(res);
    }

    [Fact]
    public async Task Login_with_expired_slug_throws_subscription_expired()
    {
        using var h = new TestHarness(marketId: null);
        var expiry = DateTime.UtcNow.AddSeconds(-1);
        var market = await AddMarketAsync(h, "alpha", expiresAt: expiry);
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<SubscriptionExpiredException>(() =>
            NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password, "alpha")));
        Assert.Equal(market.Id, ex.MarketId);
        Assert.Equal(402, ex.StatusCode);
    }

    [Fact]
    public async Task Login_with_blocked_slug_throws_market_blocked()
    {
        using var h = new TestHarness(marketId: null);
        var market = await AddMarketAsync(h, "alpha", isBlocked: true);
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<MarketBlockedException>(() =>
            NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password, "alpha")));
    }

    [Fact]
    public async Task Login_with_unknown_slug_fails()
    {
        using var h = new TestHarness(marketId: null);
        var market = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        var res = await NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password, "ghost"));
        Assert.Null(res);
    }

    // ── no-slug path (SuperAdmin / market-agnostic) ──────────────────────

    [Fact]
    public async Task Login_without_slug_still_enforces_expiry_on_matched_market()
    {
        using var h = new TestHarness(marketId: null);
        var market = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddSeconds(-1));
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        // No subdomain supplied — the door check runs off the matched user's market.
        await Assert.ThrowsAsync<SubscriptionExpiredException>(() =>
            NewAuthService(h).LoginAsync(new LoginRequest("sardor", Password)));
    }

    [Fact]
    public async Task SuperAdmin_without_market_logs_in_without_subscription_gate()
    {
        using var h = new TestHarness(marketId: null);
        await AddOwnerAsync(h, marketId: null, username: "root", role: Role.SuperAdmin);
        h.Db.ChangeTracker.Clear();

        var res = await NewAuthService(h).LoginAsync(new LoginRequest("root", Password));

        Assert.NotNull(res);
        Assert.Null(res!.MarketId);
        Assert.Null(res.Subdomain);
        Assert.Equal("SuperAdmin", res.Role);
    }

    [Fact]
    public async Task Wrong_password_still_fails_on_an_active_slug()
    {
        using var h = new TestHarness(marketId: null);
        var market = await AddMarketAsync(h, "alpha", expiresAt: DateTime.UtcNow.AddDays(30));
        await AddOwnerAsync(h, market.Id, "sardor");
        h.Db.ChangeTracker.Clear();

        var res = await NewAuthService(h).LoginAsync(new LoginRequest("sardor", "wrong-password", "alpha"));
        Assert.Null(res);
    }
}
