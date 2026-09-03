using System.Net;
using System.Net.Http.Json;
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
/// Do'kon nusxasi bulutdan kelgan ma'lumotni lokal bazaga yozadi. Bu yerdagi
/// tekshiruvlar ikkita narsani kafolatlaydi: yangi o'rnatilgan do'kon
/// ishlatishga yaroqli holga kelishi va uzilish yuz berganda o'zgarishlar
/// JIMGINA yo'qolmasligi.
/// </summary>
public class ShopSyncTests
{
    /// <summary>Bulut o'rniga: berilgan javobni qaytaradigan HTTP.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reply;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) => _reply = reply;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_reply(request));
        }
    }

    private static (ShopSyncService Service, StubHandler Handler) NewService(
        TestHarness h, Func<HttpRequestMessage, HttpResponseMessage> reply)
    {
        var handler = new StubHandler(reply);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("cloud").Returns(_ => new HttpClient(handler, disposeHandler: false));

        var options = new ShopCloudOptions { Url = "https://bulut.test/", TerminalKey = "kalit" };
        var service = new ShopSyncService(
            h.Db, h.UnitOfWork, factory, options,
            NullLogger<ShopSyncService>.Instance, h.DbClock);
        return (service, handler);
    }

    private static HttpResponseMessage Json(SyncPullDto payload) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private static SyncPullDto Payload(
        SyncMarketDto? market, params SyncUserDto[] users)
    {
        var stamp = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        return new SyncPullDto(stamp, stamp, market, users);
    }

    /// <summary>Faqat tovarlar keladigan javob — market va xodimsiz.</summary>
    private static SyncPullDto WithProducts(params SyncProductDto[] products)
    {
        var stamp = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        return new SyncPullDto(stamp, stamp, null, [], products);
    }

    /// <summary>Javobni do'kon bazasiga qo'llaydi.</summary>
    private static async Task ApplyAsync(TestHarness h, SyncPullDto payload)
    {
        var (service, _) = NewService(h, _ => Json(payload));
        var result = await service.PullAsync();
        Assert.True(result.Success, result.Error);
    }

    private static SyncMarketDto NewMarket(
        int id = 9, string name = "Taxtapul", string? subdomain = "taxtapul") =>
        new(id, name, subdomain, "Toshkent", "Start",
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            true, false, null, Guid.NewGuid(), new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    private static SyncUserDto NewUser(string username, Role role = Role.Seller, bool deleted = false) =>
        new(Guid.NewGuid(), username, "Xodim", "$2a$hash", null, (int)role, !deleted, deleted,
            Array.Empty<string>(), false, "Uzbek", null, null,
            new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Asosiy holat: bo'sh do'kon bazasi birinchi tortishdan keyin
    /// ishlatishga yaroqli bo'ladi.
    /// </summary>
    [Fact]
    public async Task Bosh_baza_birinchi_tortishdan_keyin_toladi()
    {
        using var h = new TestHarness(marketId: null);
        var (service, _) = NewService(h, _ => Json(Payload(NewMarket(), NewUser("jamshid", Role.Owner))));

        var result = await service.PullAsync();

        Assert.True(result.Success, result.Error);
        var market = await h.Db.Markets.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(9, market.Id);
        Assert.Equal("Taxtapul", market.Name);
        // «Ishlatishga yaroqli» degani AYNAN shuni ham o'z ichiga oladi:
        // manzildagi nomsiz kirishdan keyin boradigan joy yo'q va do'kon
        // dasturi kirish formasida turib qolardi. Nuqson shu yerda edi —
        // maydon ko'chirilmasdi va buni birorta sinov ushlamasdi.
        Assert.Equal("taxtapul", market.Subdomain);

        var user = await h.Db.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("jamshid", user.Username);
        Assert.Equal(9, user.MarketId);
        Assert.Equal("$2a$hash", user.PasswordHash);
    }

    /// <summary>
    /// Market ID bulutdagidek bo'lishi SHART: aks holda keyin yuboriladigan
    /// har bir savdo boshqa do'konga tegishli bo'lib qolardi.
    /// </summary>
    [Fact]
    public async Task Market_id_bulutdagidek_yoziladi()
    {
        using var h = new TestHarness(marketId: null);
        var (service, _) = NewService(h, _ => Json(Payload(NewMarket(id: 42))));

        await service.PullAsync();

        Assert.Equal(42, (await h.Db.Markets.IgnoreQueryFilters().SingleAsync()).Id);
    }

    /// <summary>
    /// Manzildagi nomi BO'SH qolgan nusxa keyingi tortishda o'zini
    /// tuzatadi.
    ///
    /// <para>Bu shunchaki nazariy holat emas: maydon sinxronizatsiyaga
    /// kiritilgunga qadar o'rnatilgan har bir nusxa aynan shunday
    /// qolgan — do'kon yozuvi bor, nomi esa yo'q. Bunday nusxada kirish
    /// o'tadi, lekin ish ekraniga o'tib bo'lmaydi.</para>
    ///
    /// <para>O'zini tuzatishi bulut do'kon yozuvini HAR safar
    /// yuborishiga bog'liq. Suv belgisi bilan yuborilganda bu ishlamasdi:
    /// belgi allaqachon oldinda edi, ya'ni yozuv boshqa kelmasdi va
    /// nusxani faqat bazani o'chirib qayta bog'lash qutqarardi.</para>
    /// </summary>
    [Fact]
    public async Task Bosh_qolgan_manzil_nomi_keyingi_tortishda_tolanadi()
    {
        using var h = new TestHarness(marketId: null);

        // Eski nuqson qoldirgan holat.
        await ApplyAsync(h, Payload(NewMarket(subdomain: null), NewUser("jamshid", Role.Owner)));
        Assert.Null((await h.Db.Markets.IgnoreQueryFilters().SingleAsync()).Subdomain);

        await ApplyAsync(h, Payload(NewMarket(subdomain: "taxtapul")));

        Assert.Equal("taxtapul", (await h.Db.Markets.IgnoreQueryFilters().SingleAsync()).Subdomain);
    }

    [Fact]
    public async Task Takroriy_tortish_nusxa_yaratmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var user = NewUser("kassir");
        var (service, _) = NewService(h, _ => Json(Payload(NewMarket(), user)));

        await service.PullAsync();
        await service.PullAsync();

        Assert.Single(await h.Db.Markets.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await h.Db.Users.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Bulutda o'chirilgan xodim do'konda ham o'chgan bo'lishi kerak — aks
    /// holda bo'shatilgan kassir kirishda davom etardi.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_xodim_dokonda_ham_ochadi()
    {
        using var h = new TestHarness(marketId: null);
        var user = NewUser("kassir");
        var (service, _) = NewService(h, _ => Json(Payload(NewMarket(), user)));
        await service.PullAsync();

        var removed = user with { IsDeleted = true, IsActive = false };
        var (second, _) = NewService(h, _ => Json(Payload(null, removed)));
        await second.PullAsync();

        var stored = await h.Db.Users.IgnoreQueryFilters().SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.False(stored.IsActive);
    }

    /// <summary>Keyingi so'rov oxirgi belgidan boshlanishi kerak.</summary>
    [Fact]
    public async Task Suv_belgisi_saqlanadi_va_qayta_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        var stamp = new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero);
        var payload = new SyncPullDto(stamp, stamp, NewMarket(), Array.Empty<SyncUserDto>());
        var (service, handler) = NewService(h, _ => Json(payload));

        await service.PullAsync();
        var state = await h.Db.SyncStates.SingleAsync();
        Assert.Equal(stamp, state.PullWatermark);

        await service.PullAsync();
        Assert.Contains(Uri.EscapeDataString(stamp.ToString("O")), handler.LastRequest!.RequestUri!.Query);
    }

    /// <summary>
    /// Internet yo'qligi do'konda NORMAL holat: savdo to'xtamasligi va
    /// sabab yozib qo'yilishi kerak.
    /// </summary>
    [Fact]
    public async Task Aloqa_yoqligida_yiqilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var (first, _) = NewService(h, _ => Json(Payload(NewMarket())));
        await first.PullAsync();

        var (broken, _) = NewService(h, _ => throw new HttpRequestException("tarmoq yo'q"));
        var result = await broken.PullAsync();

        Assert.False(result.Success);
        var state = await h.Db.SyncStates.SingleAsync();
        Assert.NotNull(state.LastError);
    }

    /// <summary>
    /// Aloqa uzilganda suv belgisi OLDINGA SURILMASLIGI shart: aks holda
    /// o'sha o'zgarishlar do'konga hech qachon yetib bormasdi.
    /// </summary>
    [Fact]
    public async Task Xatoda_suv_belgisi_surilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var stamp = new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero);
        var (first, _) = NewService(h, _ => Json(
            new SyncPullDto(stamp, stamp, NewMarket(), Array.Empty<SyncUserDto>())));
        await first.PullAsync();

        var (broken, _) = NewService(h, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await broken.PullAsync();

        Assert.Equal(stamp, (await h.Db.SyncStates.SingleAsync()).PullWatermark);
    }

    /// <summary>
    /// Bekor qilingan kalit oddiy aloqa uzilishidan farq qiladi: u o'z-o'zidan
    /// tuzalmaydi va odam aralashuvini talab qiladi.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_kalit_alohida_aytiladi()
    {
        using var h = new TestHarness(marketId: null);
        var (first, _) = NewService(h, _ => Json(Payload(NewMarket())));
        await first.PullAsync();

        var (revoked, _) = NewService(h, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var result = await revoked.PullAsync();

        Assert.False(result.Success);
        Assert.Contains("bog'lash", result.Error!);
    }

    [Fact]
    public async Task Boglanmagan_dokon_tortmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var factory = Substitute.For<IHttpClientFactory>();
        var service = new ShopSyncService(
            h.Db, h.UnitOfWork, factory, new ShopCloudOptions(),
            NullLogger<ShopSyncService>.Instance, h.DbClock);

        var result = await service.PullAsync();

        Assert.False(service.IsConfigured);
        Assert.True(result.Success);          // xato emas, shunchaki hali bog'lanmagan
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [Fact]
    public async Task Muvaffaqiyatda_eski_xato_tozalanadi()
    {
        using var h = new TestHarness(marketId: null);
        var (first, _) = NewService(h, _ => Json(Payload(NewMarket())));
        await first.PullAsync();

        var (broken, _) = NewService(h, _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        await broken.PullAsync();
        Assert.NotNull((await h.Db.SyncStates.SingleAsync()).LastError);

        var (fixedAgain, _) = NewService(h, _ => Json(Payload(NewMarket())));
        await fixedAgain.PullAsync();

        Assert.Null((await h.Db.SyncStates.SingleAsync()).LastError);
    }

    // ── Egasi masofadan o'zgartirgan narx ─────────────────────────────────
    // Ilgari bulutdagi o'zgarish do'konga HECH QACHON yetib bormasdi: kassa
    // eski narxda sotaverar, ertasiga esa do'kon o'sha tovarni yuborib,
    // bulutdagi yangi narxni jimgina eskisiga almashtirardi.

    /// <summary>Egasi narxni o'zgartirsa — do'konga yetib boradi.</summary>
    [Fact]
    public async Task Bulutdagi_narx_dokonga_yetib_boradi()
    {
        using var h = new TestHarness(marketId: null);
        var id = Guid.NewGuid();
        h.Db.Products.Add(new Product
        {
            Id = id, MarketId = 7, Name = "Sement", Quantity = 500,
            CostPrice = 40_000, SalePrice = 52_000, MinSalePrice = 50_000,
            MinThreshold = 10,
        });
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // Bulutdagi o'zgarish do'kondagidan KEYIN bo'lgan.
        await ApplyAsync(h, WithProducts(NewProduct(h, id, 58_000, afterHours: 1)));

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.Equal(58_000, stored.SalePrice);
    }

    /// <summary>
    /// ENG MUHIM chegara: QOLDIQ hech qachon bulutdan qaytmaydi. Bulutdagi
    /// son oxirgi yuborishdagi nusxa va uni qaytarish o'sha payt sotilgan
    /// tovarni «tiriltirib» yuborardi.
    /// </summary>
    [Fact]
    public async Task Qoldiq_bulutdan_qaytarilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var id = Guid.NewGuid();
        h.Db.Products.Add(new Product
        {
            Id = id, MarketId = 7, Name = "Sement", Quantity = 3,   // deyarli tugagan
            CostPrice = 40_000, SalePrice = 52_000, MinSalePrice = 50_000,
            MinThreshold = 10,
        });
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await ApplyAsync(h, WithProducts(NewProduct(h, id, 58_000, afterHours: 1)));

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.Equal(3, stored.Quantity);
        Assert.Equal(58_000, stored.SalePrice);   // narx esa o'tdi
    }

    /// <summary>Do'kondagi yozuv yangiroq bo'lsa — eski nusxa bosib ketmaydi.</summary>
    [Fact]
    public async Task Dokondagi_yangiroq_ozgarish_saqlanadi()
    {
        using var h = new TestHarness(marketId: null);
        var id = Guid.NewGuid();
        h.Db.Products.Add(new Product
        {
            Id = id, MarketId = 7, Name = "Sement", Quantity = 500,
            CostPrice = 40_000, SalePrice = 60_000, MinSalePrice = 50_000,
            MinThreshold = 10,
        });
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // Bulutdan ESKI nusxa keladi — do'kondagi o'zgarishdan oldingi.
        await ApplyAsync(h, WithProducts(NewProduct(h, id, 52_000, afterHours: -3)));

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.Equal(60_000, stored.SalePrice);
    }

    /// <summary>
    /// Yaratilgan tovar SHU do'konga yoziladi.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari noma'lum id umuman yaratilmasdi va bu sinov o'sha
    /// xulqni qulflab turardi. Endi tovar yaratiladi — lekin do'kon raqami
    /// javobdan EMAS, tortishda aniqlangan qiymatdan olinadi. Bulut buzilgan
    /// javob yuborsa ham, tovar begona do'konga tushib qolmasligi
    /// kerak.</para>
    /// </remarks>
    [Fact]
    public async Task Yaratilgan_tovar_shu_dokonga_yoziladi()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await ApplyAsync(h, WithProducts(NewProduct(h, Guid.NewGuid(), 1000, afterHours: 1)));

        var stored = Assert.Single(await h.Db.Products.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(7, stored.MarketId);
    }

    /// <summary>
    /// Bulutdan kelgan tovar. Vaqt SINOV SOATIGA nisbatan beriladi: lokal
    /// `UpdatedAt` ni `SaveChanges` aynan o'sha soat bilan bosadi, ya'ni
    /// haqiqiy vaqtga tayanish taqqoslashni ma'nosiz qilardi.
    /// </summary>
    private static SyncProductDto NewProduct(
        TestHarness h, Guid id, decimal salePrice, double afterHours,
        string name = "Sement", UnitType unit = UnitType.Piece) =>
        new(id, name, 40_000, salePrice, 50_000, 10, null, null, false, false,
            h.DbClock.GetUtcNow().AddHours(afterHours), (int)unit);

    /// <summary>
    /// Saytdan qo'shilgan tovar do'konga YETIB BORADI.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari noma'lum id shunchaki o'tkazib yuborilardi: egasi
    /// katalogga saytdan tovar qo'shsa, u do'konga hech qachon yetmasdi.
    /// Xato chiqmasdi va hech qayerga yozilmasdi — egasi tovarni panelda
    /// ko'rar, kassir esa uni kassadan topa olmasdi.</para>
    /// </remarks>
    [Fact]
    public async Task Saytdan_qoshilgan_tovar_dokonga_yetadi()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var id = Guid.NewGuid();

        await ApplyAsync(h, WithProducts(
            NewProduct(h, id, 52_000, afterHours: 0, name: "Yangi sement", unit: UnitType.Bag)));

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.Equal("Yangi sement", stored.Name);
        Assert.Equal(52_000m, stored.SalePrice);
        // Birlik ham keladi: busiz qopda sotiladigan sement kassada
        // «dona» bo'lib ko'rinardi.
        Assert.Equal(UnitType.Bag, stored.Unit);
    }

    /// <summary>
    /// Yangi tovarning qoldig'i NOL bo'ladi.
    /// </summary>
    /// <remarks>
    /// Qoldiqni faqat do'kon biladi — tovar u yerda jismonan turadi. Bulut
    /// raqamini qabul qilish omborda yo'q tovarni bor qilib ko'rsatardi va
    /// kassir buni faqat mijoz oldida bilardi. Qoldiq xaridnoma yoki
    /// inventarizatsiya orqali paydo bo'ladi.
    /// </remarks>
    [Fact]
    public async Task Yangi_tovarning_qoldigi_nol()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var id = Guid.NewGuid();

        await ApplyAsync(h, WithProducts(NewProduct(h, id, 52_000, afterHours: 0)));

        var stored = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == id);
        Assert.Equal(0m, stored.Quantity);
    }

    /// <summary>
    /// Yangi tovar OMBOR JURNALI qoidasini buzmaydi.
    /// </summary>
    /// <remarks>
    /// Qoida: <c>Quantity == SUM(jurnal Delta) − (qoralamalar ushlagani)</c>.
    /// Nol qoldiqli tovarda uchala tomon ham nol, ya'ni tenglik saqlanadi —
    /// va bulutdan kelgan tovar 1-bosqichda o'rnatilgan tekshiruvni
    /// yiqitmaydi.
    /// </remarks>
    [Fact]
    public async Task Yangi_tovar_ombor_qoidasini_buzmaydi()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState { MarketId = 7 });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await ApplyAsync(h, WithProducts(
            NewProduct(h, Guid.NewGuid(), 52_000, afterHours: 0),
            NewProduct(h, Guid.NewGuid(), 31_000, afterHours: 0, name: "G'isht")));

        h.Db.ChangeTracker.Clear();
        var drifts = await new StockReconciler(h.Db).FindDriftAsync(7);
        Assert.Empty(drifts);
    }
}
