using System.Net;
using System.Net.Http.Json;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Do'konning BIRINCHI to'ldirilishi.
///
/// <para>Bu yerdagi tekshiruvlar bitta haqiqiy nosozlikdan keyin yozildi:
/// webda ishlab kelgan do'kon desktopga o'tganda BO'SH ekran ko'rsatardi.
/// Kirish o'tardi, panel ochilardi, lekin savdo ham, tovar ham yo'q edi —
/// hammasi bulutda qolib ketgan, pastga tushadigan yo'l esa umuman
/// qurilmagan edi. Oddiy tortish faqat «bulutda nima o'zgardi» degan
/// savolga javob beradi va bu jadvallarni bilmaydi.</para>
/// </summary>
public class ShopSeedTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
        public int Calls { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_reply(request));
        }
    }

    private static ShopSyncService NewService(
        TestHarness h, Func<HttpRequestMessage, HttpResponseMessage> reply)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("cloud").Returns(
            _ => new HttpClient(new StubHandler(reply), disposeHandler: false));

        return new ShopSyncService(
            h.Db, h.UnitOfWork, factory,
            new ShopCloudOptions { Url = "https://bulut.test/", TerminalKey = "kalit" },
            NullLogger<ShopSyncService>.Instance, h.DbClock);
    }

    private static readonly DateTimeOffset Stamp = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Do'kon o'z raqamini va xodimini biladigan holatga keltiradi.</summary>
    private static async Task<Guid> PrepareAsync(TestHarness h, int marketId = 9)
    {
        var ownerId = Guid.NewGuid();
        var market = new SyncMarketDto(marketId, "Taxtapul", "taxtapul-stroy", "Toshkent", "Start",
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), true, false, null, ownerId, Stamp);
        var owner = new SyncUserDto(ownerId, "jamshid", "Ega", "$2a$hash", null, (int)Role.Owner,
            true, false, Array.Empty<string>(), false, "uz", null, null, Stamp);

        var service = NewService(h, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncPullDto(Stamp, Stamp, market, [owner])),
        });
        var pull = await service.PullAsync();
        Assert.True(pull.Success, pull.Error);
        return ownerId;
    }

    /// <summary>Jadval nomini so'rov manzilidan oladi.</summary>
    private static string TableOf(HttpRequestMessage r)
    {
        var q = System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query);
        return q["table"] ?? string.Empty;
    }

    private static int AfterOf(HttpRequestMessage r)
    {
        var q = System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query);
        return int.TryParse(q["after"], out var n) ? n : 0;
    }

    /// <summary>Bitta jadval to'ldirilgan, qolganlari bo'sh javob.</summary>
    private static HttpResponseMessage Page(
        string table, SyncPushDto data, int total, string? nextAfter = null)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(
                new SyncSnapshotDto(table, nextAfter, total, data), options: EntityWireFormat.Options),
        };

    private static HttpResponseMessage Empty(string table)
        => Page(table, new SyncPushDto(), total: 0);

    // ── Sinov ma'lumoti ──────────────────────────────────────────────────
    private static Sale NewSale(int marketId, Guid sellerId, decimal total) => new()
    {
        Id = Guid.NewGuid(),
        MarketId = marketId,
        SellerId = sellerId,
        TotalAmount = total,
        PaidAmount = total,
        Status = SaleStatus.Paid,
        CreatedAt = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc),
    };

    private static Product NewProduct(int marketId, string name, decimal qty) => new()
    {
        Id = Guid.NewGuid(),
        MarketId = marketId,
        Name = name,
        Quantity = qty,
        SalePrice = 50_000,
        CostPrice = 40_000,
    };

    /// <summary>
    /// ASOSIY holat: bulutdagi savdo va tovar do'konga tushadi.
    /// </summary>
    [Fact]
    public async Task Bulutdagi_savdo_va_tovar_dokonga_tushadi()
    {
        using var h = new TestHarness(marketId: null);
        var ownerId = await PrepareAsync(h);

        var product = NewProduct(9, "Sement", 120);
        var sale = NewSale(9, ownerId, 522_000);

        var service = NewService(h, r => TableOf(r) switch
        {
            SnapshotTables.Products => Page(SnapshotTables.Products,
                new SyncPushDto { Products = [product] }, total: 1),
            SnapshotTables.Sales => Page(SnapshotTables.Sales,
                new SyncPushDto { Sales = [sale] }, total: 1),
            var t => Empty(t),
        });

        var result = await service.SeedAsync();

        Assert.True(result.Success, result.Error);
        Assert.True(result.Completed);
        Assert.Equal(2, result.Rows);

        Assert.Equal("Sement", (await h.Db.Products.IgnoreQueryFilters().SingleAsync()).Name);
        Assert.Equal(522_000, (await h.Db.Sales.IgnoreQueryFilters().SingleAsync()).TotalAmount);
    }

    /// <summary>
    /// Qoldiq ham tushadi.
    ///
    /// <para>Oddiy tortishda qoldiq ATAYLAB yo'q — uni faqat do'kon biladi.
    /// Lekin BIRINCHI to'ldirishda do'kon hech narsa bilmaydi va bulutdagi
    /// son yagona manba: usiz butun ombor nol qoldiq bilan ochilar va
    /// kassir birorta tovarni sota olmasdi.</para>
    /// </summary>
    [Fact]
    public async Task Birinchi_toldirishda_qoldiq_ham_tushadi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var service = NewService(h, r => TableOf(r) == SnapshotTables.Products
            ? Page(SnapshotTables.Products,
                new SyncPushDto { Products = [NewProduct(9, "Sement", 120)] }, total: 1)
            : Empty(TableOf(r)));

        await service.SeedAsync();

        Assert.Equal(120, (await h.Db.Products.IgnoreQueryFilters().SingleAsync()).Quantity);
    }

    /// <summary>
    /// Nusxa bo'lak-bo'lak keladi va uzilgan joydan davom etadi.
    /// </summary>
    [Fact]
    public async Task Bolaklar_ketma_ket_olinadi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var first = NewProduct(9, "Sement", 10);
        var second = NewProduct(9, "G'isht", 20);

        var service = NewService(h, r =>
        {
            if (TableOf(r) != SnapshotTables.Products) return Empty(TableOf(r));
            return AfterOf(r) == 0
                ? Page(SnapshotTables.Products, new SyncPushDto { Products = [first] },
                       total: 2, nextAfter: "1")
                : Page(SnapshotTables.Products, new SyncPushDto { Products = [second] }, total: 2);
        });

        var result = await service.SeedAsync();

        Assert.True(result.Completed);
        Assert.Equal(2, await h.Db.Products.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>
    /// Aloqa uzilsa nusxa NOLDAN boshlanmaydi.
    ///
    /// <para>Uzilish do'konda normal holat. Har safar noldan boshlansa,
    /// sekin aloqadagi katta do'konda nusxa hech qachon oxiriga
    /// yetmasdi.</para>
    /// </summary>
    [Fact]
    public async Task Uzilishdan_keyin_qoldigidan_davom_etadi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var first = NewProduct(9, "Sement", 10);

        // Birinchi urinish: bitta bo'lak keladi, keyingisida aloqa uziladi.
        var failing = NewService(h, r =>
        {
            if (TableOf(r) != SnapshotTables.Products) return Empty(TableOf(r));
            return AfterOf(r) == 0
                ? Page(SnapshotTables.Products, new SyncPushDto { Products = [first] },
                       total: 2, nextAfter: "1")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var broken = await failing.SeedAsync();
        Assert.False(broken.Success);
        Assert.Single(await h.Db.Products.IgnoreQueryFilters().ToListAsync());

        // Holat saqlangan: keyingi urinish AYNAN o'sha joydan so'raydi.
        var state = await h.Db.SyncStates.SingleAsync();
        Assert.Equal(SnapshotTables.Products, state.SeedTable);
        Assert.Equal("1", state.SeedAfter);
        Assert.Null(state.SeedCompletedAtUtc);

        var asked = new List<int>();
        var second = NewService(h, r =>
        {
            if (TableOf(r) != SnapshotTables.Products) return Empty(TableOf(r));
            asked.Add(AfterOf(r));
            return Page(SnapshotTables.Products,
                new SyncPushDto { Products = [NewProduct(9, "G'isht", 20)] }, total: 2);
        });

        var done = await second.SeedAsync();

        Assert.True(done.Completed);
        Assert.Equal([1], asked);   // noldan EMAS
        Assert.Equal(2, await h.Db.Products.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>
    /// Nusxa AYNAN bir marta olinadi.
    ///
    /// <para>Takrorlansa, do'konda allaqachon sotilgan tovarning eski
    /// qoldig'i qaytib kelardi va kassir omborda yo'q narsani sotishga
    /// urinardi.</para>
    /// </summary>
    [Fact]
    public async Task Tugagandan_keyin_qayta_olinmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var calls = 0;
        var service = NewService(h, r => { calls++; return Empty(TableOf(r)); });

        Assert.True((await service.SeedAsync()).Completed);
        var afterFirst = calls;
        Assert.True(afterFirst > 0);

        Assert.True((await service.SeedAsync()).Completed);

        Assert.Equal(afterFirst, calls);   // bitta ham yangi so'rov yo'q
        Assert.NotNull((await h.Db.SyncStates.SingleAsync()).SeedCompletedAtUtc);
    }

    /// <summary>
    /// Mavjud yozuv ustiga YOZILMAYDI.
    ///
    /// <para>Bo'lak uzilishdan keyin qayta kelishi mumkin. Do'kondagi
    /// yangiroq holatni eski nusxa bilan bosib yuborish — sotilgan tovarni
    /// «tiriltirish» bilan barobar.</para>
    /// </summary>
    [Fact]
    public async Task Mavjud_yozuv_ustiga_yozilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var product = NewProduct(9, "Sement", 120);
        h.Db.Products.Add(new Product
        {
            Id = product.Id, MarketId = 9, Name = "Sement", Quantity = 5,
            SalePrice = 50_000, CostPrice = 40_000,
        });
        await h.Db.SaveChangesAsync();

        var service = NewService(h, r => TableOf(r) == SnapshotTables.Products
            ? Page(SnapshotTables.Products, new SyncPushDto { Products = [product] }, total: 1)
            : Empty(TableOf(r)));

        var result = await service.SeedAsync();

        Assert.True(result.Completed);
        Assert.Equal(0, result.Rows);
        // Do'kondagi qoldiq TEGILMAGAN.
        Assert.Equal(5, (await h.Db.Products.IgnoreQueryFilters().SingleAsync()).Quantity);
    }

    /// <summary>
    /// Tortish bo'lmagan do'konda nusxa BOSHLANMAYDI: market raqami ham,
    /// xodimlar ham yo'q va kelgan savdoni bog'lab bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Tortishsiz_boshlanmaydi()
    {
        using var h = new TestHarness(marketId: null);

        var calls = 0;
        var service = NewService(h, r => { calls++; return Empty(TableOf(r)); });

        var result = await service.SeedAsync();

        Assert.True(result.Success);
        Assert.Equal(0, calls);
    }

    /// <summary>
    /// Hamma jadval so'raladi — biri tushib qolsa, o'sha ma'lumot do'konga
    /// HECH QACHON yetib bormaydi va buni hech kim sezmaydi.
    /// </summary>
    [Fact]
    public async Task Hamma_jadval_soraladi()
    {
        using var h = new TestHarness(marketId: null);
        await PrepareAsync(h);

        var seen = new List<string>();
        var service = NewService(h, r => { seen.Add(TableOf(r)); return Empty(TableOf(r)); });

        await service.SeedAsync();

        Assert.Equal(SnapshotTables.InOrder, seen);
    }
}
