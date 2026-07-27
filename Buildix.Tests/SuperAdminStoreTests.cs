using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// S2 — «Магазины» ro'yxati va kartochkasi. Bu ekrandan do'kon bloklanadi,
/// shuning uchun undagi holat va raqamlar noto'g'ri bo'lsa, operator ishlab
/// turgan do'konni yopib qo'yishi mumkin.
/// </summary>
public class SuperAdminStoreTests
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

    private static (SuperAdminStoreService Service, TestHarness H) NewService()
    {
        var h = new TestHarness(marketId: null);
        return (new SuperAdminStoreService(h.Db, new FixedClock()), h);
    }

    private static Guid SeedMarket(
        TestHarness h, int id, string name, DateTime? expiresAt,
        bool blocked = false, bool active = true, string? city = null)
    {
        var ownerId = Guid.NewGuid();
        h.Db.Users.Add(new User
        {
            Id = ownerId,
            FullName = $"{name} egasi",
            Username = $"owner{id}",
            PasswordHash = "x",
            Phone = $"+99890000000{id}",
            Role = Role.Owner,
            MarketId = id,
            IsActive = true,
            LastActiveAt = Now.AddHours(-1),
        });
        h.Db.Markets.Add(new Market
        {
            Id = id,
            Name = name,
            City = city,
            Subdomain = $"m{id}",
            IsActive = active,
            IsBlocked = blocked,
            ExpiresAt = expiresAt,
            CreatedAt = Now.AddMonths(-3),
            OwnerId = ownerId,
        });
        h.Db.SaveChanges();
        return ownerId;
    }

    [Fact]
    public async Task The_list_reports_each_store_in_exactly_one_state()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Faol", Now.AddMonths(1));
        SeedMarket(h, 2, "Kechikkan", Now.AddDays(-3));
        // Bloklangan do'konning muddati ham o'tgan — lekin u «Blocked» bo'lib
        // ko'rinishi shart: aks holda operator uni to'lov bilan hal qilinadi
        // deb o'ylab, blokni ochishni unutardi.
        SeedMarket(h, 3, "Bloklangan", Now.AddDays(-10), blocked: true);

        var rows = await service.ListAsync();

        Assert.Equal("Active", rows.Single(r => r.MarketId == 1).Status);
        Assert.Equal("Overdue", rows.Single(r => r.MarketId == 2).Status);
        Assert.Equal("Blocked", rows.Single(r => r.MarketId == 3).Status);
    }

    [Fact]
    public async Task A_deleted_store_is_absent_from_the_list_and_the_card()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Tirik", Now.AddMonths(1));
        SeedMarket(h, 2, "O'chirilgan", Now.AddMonths(1), active: false);

        Assert.Single(await service.ListAsync());
        // Kartochka ham ochilmaydi — o'chirilgan do'konni bloklash yoki
        // yoqish mumkin bo'lmasligi kerak.
        Assert.Null(await service.GetAsync(2));
    }

    [Fact]
    public async Task The_row_carries_the_owner_and_their_phone()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Do'kon", Now.AddMonths(1), city: "Samarqand");

        var row = Assert.Single(await service.ListAsync());

        Assert.Equal("Do'kon egasi", row.OwnerName);
        Assert.Equal("+998900000001", row.OwnerPhone);
        Assert.Equal("Samarqand", row.City);
        // Yangi do'kon eng past tarifda ochiladi (Market.Plan default'i).
        Assert.Equal("Start", row.Plan);
    }

    [Fact]
    public async Task Receipts_this_month_ignore_drafts_cancellations_and_last_month()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Do'kon", Now.AddMonths(1));

        void Sale(SaleStatus status, DateTime at, bool deleted = false) =>
            h.Db.Sales.Add(new Sale
            {
                Id = Guid.NewGuid(),
                MarketId = 1,
                Status = status,
                CreatedAt = at,
                IsDeleted = deleted,
            });

        Sale(SaleStatus.Paid, Now.AddDays(-1));          // ✔
        Sale(SaleStatus.Debt, Now.AddDays(-2));          // ✔ qarzga sotuv ham chek
        Sale(SaleStatus.Closed, Now.AddDays(-3));        // ✔
        Sale(SaleStatus.Draft, Now.AddDays(-1));         // ✗ hali yakunlanmagan
        Sale(SaleStatus.Cancelled, Now.AddDays(-1));     // ✗ bekor qilingan
        Sale(SaleStatus.Paid, Now.AddDays(-1), true);    // ✗ o'chirilgan
        Sale(SaleStatus.Paid, new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc)); // ✗ o'tgan oy
        h.Db.SaveChanges();

        var detail = await service.GetAsync(1);

        Assert.Equal(3, detail!.Stats.ChecksThisMonth);
    }

    [Fact]
    public async Task The_month_boundary_follows_Tashkent_not_UTC()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Do'kon", Now.AddMonths(1));

        // 30-iyun 20:00 UTC = 1-iyul 01:00 Toshkent → iyul cheki.
        h.Db.Sales.Add(new Sale
        {
            Id = Guid.NewGuid(), MarketId = 1, Status = SaleStatus.Paid,
            CreatedAt = new DateTime(2026, 6, 30, 20, 0, 0, DateTimeKind.Utc),
        });
        // 30-iyun 10:00 UTC = 30-iyun 15:00 Toshkent → iyun cheki.
        h.Db.Sales.Add(new Sale
        {
            Id = Guid.NewGuid(), MarketId = 1, Status = SaleStatus.Paid,
            CreatedAt = new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc),
        });
        h.Db.SaveChanges();

        var detail = await service.GetAsync(1);

        Assert.Equal(1, detail!.Stats.ChecksThisMonth);
    }

    [Fact]
    public async Task Another_markets_data_never_leaks_into_a_card()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, "Birinchi", Now.AddMonths(1));
        SeedMarket(h, 2, "Ikkinchi", Now.AddMonths(1));
        h.Db.Sales.Add(new Sale
        {
            Id = Guid.NewGuid(), MarketId = 2, Status = SaleStatus.Paid, CreatedAt = Now.AddDays(-1),
        });
        h.Db.SaveChanges();

        // Konsolda tenant filtri O'CHIQ — har so'rov marketni o'zi filtrlashi
        // shart. Bu test aynan shuni ushlaydi.
        var first = await service.GetAsync(1);
        var second = await service.GetAsync(2);

        Assert.Equal(0, first!.Stats.ChecksThisMonth);
        Assert.Equal(1, second!.Stats.ChecksThisMonth);
        Assert.Equal(1, first.Stats.Users);
    }
}
