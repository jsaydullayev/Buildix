using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// S1 — «Панель Buildix» agregati. Panel operatorning birinchi ekrani: undagi
/// raqam noto'g'ri bo'lsa, muddati o'tgan do'kon e'tibordan chetda qoladi.
/// </summary>
public class SuperAdminDashboardTests
{
    private static readonly DateTime Now = new(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : Buildix.Application.Interfaces.ITashkentClock
    {
        public DateTime UtcNow => Now;
        public DateTime NowLocal => Now.AddHours(5);
        public DateTime TodayLocal => new(2026, 7, 26);
        public (DateTime UtcStart, DateTime UtcEnd) LocalDayToUtcRange(DateTime localDate)
            => (DateTime.SpecifyKind(localDate.Date.AddHours(-5), DateTimeKind.Utc),
                DateTime.SpecifyKind(localDate.Date.AddHours(19), DateTimeKind.Utc));
        public DateTime ToLocal(DateTime utc) => utc.AddHours(5);
    }

    private static (SuperAdminDashboardService Service, TestHarness H) NewService()
    {
        var h = new TestHarness(marketId: null);
        return (new SuperAdminDashboardService(h.Db, new FixedClock(), FixedPlatformSettings.Default), h);
    }

    private static Market SeedMarket(
        TestHarness h, int id, string name, DateTime? expiresAt,
        bool blocked = false, bool active = true, DateTime? createdAt = null)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(),
            FullName = $"{name} egasi",
            Username = $"owner{id}",
            PasswordHash = "x",
            Role = Role.Owner,
            MarketId = id,
            IsActive = true,
            LastActiveAt = Now.AddHours(-id),
        };
        var m = new Market
        {
            Id = id,
            Name = name,
            Subdomain = $"m{id}",
            IsActive = active,
            IsBlocked = blocked,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt ?? Now.AddMonths(-6),
            OwnerId = owner.Id,
        };
        h.Db.Markets.Add(m);
        h.Db.Users.Add(owner);
        h.Db.SaveChanges();
        return m;
    }

    [Fact]
    public async Task Kpis_and_lists_split_stores_by_their_real_state()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Faol do'kon", Now.AddMonths(2));
        SeedMarket(h, 2, "Muddati o'tgan", Now.AddDays(-6));
        SeedMarket(h, 3, "Bloklangan", Now.AddMonths(1), blocked: true);
        SeedMarket(h, 4, "Muddati yaqin", Now.AddDays(3));

        var d = await service.GetAsync();

        // Bloklangan «faol» emas, muddati o'tgan ham emas — u alohida holat.
        Assert.Equal(2, d.Kpis.ActiveStores); // 1 va 4
        Assert.Equal(1, d.Kpis.OverdueStores);
        Assert.Equal("Muddati o'tgan", Assert.Single(d.Overdue).Name);
        // Bloklangani va allaqachon o'tgani «yaqin»da chiqmaydi — ular qizil blokda.
        Assert.Equal("Muddati yaqin", Assert.Single(d.ExpiringSoon).Name);
        Assert.Equal(4, d.Stores.Count);
    }

    [Fact]
    public async Task A_deleted_store_disappears_from_every_number()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Tirik", Now.AddMonths(1));
        // DeleteOwner soft-delete qiladi: Market.IsActive = false. Bunday do'kon
        // platformada yo'q hisoblanadi, aks holda o'chirilgan do'kon panelda
        // «muddati o'tgan» bo'lib abadiy osilib turardi.
        SeedMarket(h, 2, "O'chirilgan", Now.AddDays(-30), active: false);

        var d = await service.GetAsync();

        Assert.Equal(1, d.Kpis.ActiveStores);
        Assert.Equal(0, d.Kpis.OverdueStores);
        Assert.Single(d.Stores);
    }

    [Fact]
    public async Task A_store_with_no_expiry_is_never_overdue()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Muddatsiz", expiresAt: null);

        var d = await service.GetAsync();

        // ExpiresAt = null → «grandfather» (TZ-sub-path-login-va-obuna §2).
        Assert.Equal(1, d.Kpis.ActiveStores);
        Assert.Empty(d.Overdue);
        Assert.Empty(d.ExpiringSoon);
    }

    [Fact]
    public async Task This_months_new_stores_are_counted_on_the_Tashkent_calendar()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Eski", Now.AddMonths(1), createdAt: Now.AddMonths(-2));
        SeedMarket(h, 2, "Shu oyda", Now.AddMonths(1), createdAt: new DateTime(2026, 7, 3, 6, 0, 0, DateTimeKind.Utc));
        // 1-iyul 02:00 UTC = 1-iyul 07:00 Toshkent → shu oyga kiradi.
        SeedMarket(h, 3, "Oy boshi", Now.AddMonths(1), createdAt: new DateTime(2026, 7, 1, 2, 0, 0, DateTimeKind.Utc));
        // 30-iyun 20:00 UTC = 1-iyul 01:00 Toshkent → mahalliy kalendar bo'yicha
        // ham shu oyga kiradi (server UTC'da tursa ham iyunga tushib qolmaydi).
        SeedMarket(h, 4, "Chegara", Now.AddMonths(1), createdAt: new DateTime(2026, 6, 30, 20, 0, 0, DateTimeKind.Utc));

        var d = await service.GetAsync();

        Assert.Equal(3, d.Kpis.NewStoresThisMonth);
    }

    [Fact]
    public async Task Only_pending_requests_reach_the_panel()
    {
        var (service, h) = NewService();
        foreach (var (status, name) in new[]
                 {
                     (RegistrationRequestStatus.Pending, "Yangi"),
                     (RegistrationRequestStatus.Accepted, "Qabul qilingan"),
                     (RegistrationRequestStatus.Rejected, "Rad etilgan"),
                     (RegistrationRequestStatus.Approved, "Ulangan"),
                 })
        {
            h.Db.RegistrationRequests.Add(new RegistrationRequest
            {
                Id = Guid.NewGuid(),
                FullName = name,
                Phone = "+998900000000",
                Status = status,
                CreatedAt = Now,
            });
        }
        h.Db.SaveChanges();

        var d = await service.GetAsync();

        // Panelda faqat ish talab qiladigani — qolganlari Заявки ekranida.
        Assert.Equal(1, d.Kpis.NewRequests);
        Assert.Equal("Yangi", Assert.Single(d.NewRequests).FullName);
    }

    [Fact]
    public async Task Revenue_counts_only_the_stores_that_are_actually_being_served()
    {
        var (service, h) = NewService();
        h.Db.PlatformPlans.AddRange(
            new PlatformPlan { Code = PlanCode.Start, PriceUzs = 600_000m, MaxUsers = 3, MaxPoints = 1 },
            new PlatformPlan { Code = PlanCode.Pro, PriceUzs = 2_400_000m, MaxUsers = 0, MaxPoints = 3 });
        h.Db.SaveChanges();

        SeedMarket(h, 1, "Faol Start", Now.AddMonths(1));
        h.Db.Markets.Single(m => m.Id == 1).Plan = PlanCode.Start;
        SeedMarket(h, 2, "Faol Pro", Now.AddMonths(1));
        h.Db.Markets.Single(m => m.Id == 2).Plan = PlanCode.Pro;
        // To'lamayotgan va bloklangan do'kon oylik daromadga KIRMAYDI — aks
        // holda KPI kutilayotgan pulni oshirib ko'rsatardi.
        SeedMarket(h, 3, "Kechikkan", Now.AddDays(-1));
        SeedMarket(h, 4, "Bloklangan", Now.AddMonths(1), blocked: true);
        h.Db.SaveChanges();

        var d = await service.GetAsync();

        Assert.Equal(3_000_000m, d.Kpis.MonthlyRevenueUzs);
    }
}
