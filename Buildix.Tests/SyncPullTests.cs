using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// Bulutdan do'konga tortish. Bu yerda ikkita narsa hal qiluvchi: suv
/// belgisi HECH BIR o'zgarishni o'tkazib yubormasligi va boshqa do'konning
/// ma'lumoti umuman chiqmasligi.
/// </summary>
public class SyncPullTests
{
    private static readonly DateTime Beginning = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SyncPullService NewService(TestHarness h) => new(h.Db, h.DbClock);

    private static async Task<Market> NewMarketAsync(TestHarness h, int id, string name)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(), MarketId = id, Username = $"owner{id}",
            FullName = "Ega", PasswordHash = "hash", Role = Role.Owner,
        };
        var market = new Market { Id = id, Name = name, OwnerId = owner.Id };
        h.Db.Users.Add(owner);
        h.Db.Markets.Add(market);
        await h.Db.SaveChangesAsync();
        return market;
    }

    [Fact]
    public async Task Yangi_dokon_hamma_narsani_oladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Sement Savdo");

        var pull = await NewService(h).PullAsync(market.Id, Beginning);

        Assert.NotNull(pull.Market);
        Assert.Equal("Sement Savdo", pull.Market!.Name);
        Assert.Single(pull.Users);
        Assert.Equal("owner5", pull.Users[0].Username);
    }

    /// <summary>
    /// Do'kon kirishni o'zi tekshiradi, ya'ni parol hash'isiz ishlay olmaydi.
    /// </summary>
    [Fact]
    public async Task Parol_hashi_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");

        var pull = await NewService(h).PullAsync(market.Id, Beginning);

        Assert.Equal("hash", pull.Users[0].PasswordHash);
    }

    /// <summary>Eng muhim chegara: boshqa do'konning xodimi chiqmasligi kerak.</summary>
    [Fact]
    public async Task Boshqa_dokonning_malumoti_chiqmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var mine = await NewMarketAsync(h, 5, "Meniki");
        await NewMarketAsync(h, 6, "Qo'shni");

        var pull = await NewService(h).PullAsync(mine.Id, Beginning);

        Assert.Single(pull.Users);
        Assert.Equal("owner5", pull.Users[0].Username);
        Assert.Equal(5, pull.Market!.Id);
    }

    [Fact]
    public async Task Ozgarmagan_bolsa_bosh_qaytadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");
        var first = await NewService(h).PullAsync(market.Id, Beginning);

        h.DbClock.Advance(TimeSpan.FromHours(1));
        var second = await NewService(h).PullAsync(market.Id, first.NextSince.AddTicks(1));

        Assert.Null(second.Market);
        Assert.Empty(second.Users);
    }

    [Fact]
    public async Task Faqat_ozgargani_qaytadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");
        var first = await NewService(h).PullAsync(market.Id, Beginning);

        h.DbClock.Advance(TimeSpan.FromHours(2));
        h.Db.Users.Add(new User
        {
            Id = Guid.NewGuid(), MarketId = 5, Username = "kassir",
            FullName = "Kassir", PasswordHash = "h2", Role = Role.Seller,
        });
        await h.Db.SaveChangesAsync();

        var second = await NewService(h).PullAsync(market.Id, first.NextSince.AddTicks(1));

        Assert.Single(second.Users);
        Assert.Equal("kassir", second.Users[0].Username);
        Assert.Null(second.Market);   // do'konning o'zi o'zgarmadi
    }

    /// <summary>
    /// O'chirilgan xodim ham tushishi SHART. Aks holda bo'shatilgan kassir
    /// do'konda abadiy ishlayverardi — bulutda o'chirilgani bilan.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_xodim_ham_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");
        var seller = new User
        {
            Id = Guid.NewGuid(), MarketId = 5, Username = "kassir",
            FullName = "Kassir", PasswordHash = "h2", Role = Role.Seller,
        };
        h.Db.Users.Add(seller);
        await h.Db.SaveChangesAsync();
        var first = await NewService(h).PullAsync(market.Id, Beginning);

        h.DbClock.Advance(TimeSpan.FromHours(1));
        seller.IsDeleted = true;
        await h.Db.SaveChangesAsync();

        var second = await NewService(h).PullAsync(market.Id, first.NextSince.AddTicks(1));

        var row = Assert.Single(second.Users);
        Assert.Equal("kassir", row.Username);
        Assert.True(row.IsDeleted);
    }

    /// <summary>
    /// Bitta saqlashda o'zgargan yozuvlar AYNAN bir xil vaqt oladi. Suv
    /// belgisi qat'iy «katta» bo'lsa, ularning bir qismi butunlay o'tkazib
    /// yuborilardi — va buni hech kim sezmasdi.
    /// </summary>
    [Fact]
    public async Task Bir_vaqtda_ozgargan_yozuvlar_yoqolmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");

        h.DbClock.Advance(TimeSpan.FromHours(1));
        for (var i = 0; i < 3; i++)
        {
            h.Db.Users.Add(new User
            {
                Id = Guid.NewGuid(), MarketId = 5, Username = $"kassir{i}",
                FullName = "Kassir", PasswordHash = "h", Role = Role.Seller,
            });
        }
        await h.Db.SaveChangesAsync();   // uchalasi bir xil UpdatedAt oladi

        var pull = await NewService(h).PullAsync(market.Id, Beginning);

        Assert.Equal(4, pull.Users.Count);   // ega + uchta kassir
        // Belgi eng katta vaqtga teng va uni QAYTA so'rasa hammasi qaytadi:
        // takror zarar qilmaydi, yo'qolish esa qaytarib bo'lmas.
        var again = await NewService(h).PullAsync(market.Id, pull.NextSince);
        Assert.Equal(3, again.Users.Count);
    }

    /// <summary>
    /// Sinxronizatsiya vaqtlari SIMDA o'z siljishini olib yurishi shart.
    ///
    /// <para>Bu test haqiqiy nuqsondan keyin yozildi. API qolgan hamma joyda
    /// vaqtni Toshkent mintaqasida va belgisiz yuboradi (interfeys uchun
    /// ataylab). Sinxronizatsiya kanalida esa bu ikki xatoni birdan
    /// keltirib chiqargan edi: do'kon o'zi olgan belgini qaytarganda
    /// Npgsql uni UTC ustuni bilan solishtirishdan bosh tortardi (400), va
    /// undan ham yomoni — belgi ustunlardan 5 soat oldinda bo'lgani uchun
    /// do'kon har sinxronizatsiyada 5 soatlik o'zgarishni JIMGINA o'tkazib
    /// yuborardi.</para>
    ///
    /// <para><c>DateTimeOffset</c> bu muammoni turi bilan hal qiladi. Agar
    /// kimdir uni <c>DateTime</c> ga qaytarsa, bu test kompilyatsiyadan
    /// o'tmaydi.</para>
    /// </summary>
    [Fact]
    public async Task Vaqtlar_siljishi_bilan_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");

        var pull = await NewService(h).PullAsync(market.Id, Beginning);

        Assert.Equal(TimeSpan.Zero, pull.NextSince.Offset);
        Assert.Equal(TimeSpan.Zero, pull.ServerTimeUtc.Offset);
        Assert.Equal(TimeSpan.Zero, pull.Market!.UpdatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, pull.Users[0].UpdatedAt.Offset);

        // JSON da siljish ochiq ko'rinishi kerak — mijoz uni taxmin
        // qilmasligi shart.
        var json = System.Text.Json.JsonSerializer.Serialize(pull);
        Assert.Contains("+00:00", json);
    }

    /// <summary>
    /// Do'kon belgini QANDAY mintaqada qaytarsa ham natija bir xil bo'lishi
    /// kerak: siljish qiymatning bir qismi, uni alohida talqin qilish
    /// kerak emas.
    /// </summary>
    [Fact]
    public async Task Belgi_mintaqasi_natijaga_tasir_qilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");
        var first = await NewService(h).PullAsync(market.Id, Beginning);

        // Ayni bir lahza, ikki xil yozuv.
        var utc = first.NextSince.AddTicks(1);
        var tashkent = utc.ToOffset(TimeSpan.FromHours(5));

        var a = await NewService(h).PullAsync(market.Id, utc);
        var b = await NewService(h).PullAsync(market.Id, tashkent);

        Assert.Equal(a.Users.Count, b.Users.Count);
        Assert.Equal(a.Market is null, b.Market is null);
    }

    [Fact]
    public async Task Ruxsatlar_ham_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h, 5, "Do'kon");
        h.Db.Users.Add(new User
        {
            Id = Guid.NewGuid(), MarketId = 5, Username = "admin",
            FullName = "Admin", PasswordHash = "h", Role = Role.Admin,
            IsPermissionsCustomized = true,
            Permissions = new List<string> { "sales.create", "products.edit" },
        });
        await h.Db.SaveChangesAsync();

        var pull = await NewService(h).PullAsync(market.Id, Beginning);

        var admin = pull.Users.Single(u => u.Username == "admin");
        Assert.True(admin.IsPermissionsCustomized);
        Assert.Contains("sales.create", admin.Permissions);
        Assert.Contains("products.edit", admin.Permissions);
    }
}
