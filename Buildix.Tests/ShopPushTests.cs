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
/// Do'kon tomoni: o'zgargan yozuvlarni bulutga yuborish. Bu yerdagi
/// tekshiruvlar ikkita jimgina xatoga qarshi turadi — navbatning qotib
/// qolishi va yozuvning abadiy yo'qolishi.
/// </summary>
public class ShopPushTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<SyncPushDto, HttpResponseMessage> _reply;
        public List<SyncPushDto> Sent { get; } = new();

        public StubHandler(Func<SyncPushDto, HttpResponseMessage> reply) => _reply = reply;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadFromJsonAsync<SyncPushDto>(
                EntityWireFormat.Options, cancellationToken);
            Sent.Add(body!);
            return _reply(body!);
        }
    }

    private static HttpResponseMessage Accepted(SyncPushDto payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncPushResultDto(
                payload.TotalRows,
                new Dictionary<string, int>(),
                new Dictionary<string, int>())),
        };

    private static (ShopPushService Service, StubHandler Handler) NewService(
        TestHarness h, Func<SyncPushDto, HttpResponseMessage>? reply = null)
    {
        var handler = new StubHandler(reply ?? Accepted);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("cloud").Returns(_ => new HttpClient(handler, disposeHandler: false));

        var options = new ShopCloudOptions { Url = "https://bulut.test/", TerminalKey = "kalit" };
        return (new ShopPushService(
            h.Db, h.UnitOfWork, factory, options,
            NullLogger<ShopPushService>.Instance, h.DbClock), handler);
    }

    /// <summary>Do'kon o'z raqamini bulutdan biladi — usiz yuborib bo'lmaydi.</summary>
    private static async Task ReadyAsync(TestHarness h, int marketId = 9)
    {
        h.Db.SyncStates.Add(new SyncState
        {
            MarketId = marketId,
            PullWatermark = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        await h.Db.SaveChangesAsync();
    }

    private static Product NewProduct(string name, int marketId = 9) => new()
    {
        Id = Guid.NewGuid(), Name = name, MarketId = marketId,
        Unit = UnitType.Piece, Quantity = 5, SalePrice = 100,
    };

    [Fact]
    public async Task Ozgargan_yozuvlar_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        h.Db.Products.Add(NewProduct("Sement"));
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        var result = await service.PushAsync();

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Rows);
        Assert.Single(handler.Sent[0].Products);
        Assert.Equal("Sement", handler.Sent[0].Products[0].Name);
    }

    /// <summary>
    /// BULUTDAN kelgan qator qaytib yuqoriga KETMAYDI.
    /// </summary>
    /// <remarks>
    /// <para>Aylanish shunday tug'iladi: pastga tushgan qator do'kon
    /// bazasiga yozilganda o'zining lokal <c>UpdatedAt</c> ini oladi, ya'ni
    /// darhol «yangi o'zgargan» bo'lib ko'rinadi va yuborishga tushadi.
    /// Bulut uni qabul qilib o'z vaqtini qo'yadi, ikkinchi kassa yana
    /// tortadi, yana yozadi, yana yuboradi — CHEKSIZ AYLANISH. Xato
    /// chiqmaydi: tarmoq va baza bekorga ishlaydi.</para>
    /// </remarks>
    [Fact]
    public async Task Bulutdan_kelgan_qator_qaytib_ketmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        var product = NewProduct("Bulutdan kelgan");
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();

        // Qator bulutdan kelgan deb belgilanadi — AYNAN shu holati bilan.
        var applied = await h.Db.Products.IgnoreQueryFilters()
            .Where(x => x.Id == product.Id).Select(x => x.UpdatedAt).FirstAsync();
        h.Db.SyncedRowMarks.Add(new SyncedRowMark
        {
            RowId = product.Id, TableName = nameof(Product), AppliedUpdatedAt = applied,
        });
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        var result = await service.PushAsync();

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, result.Rows);
        Assert.Empty(handler.Sent);
    }

    /// <summary>
    /// Bulutdan kelgan qator DO'KONDA o'zgartirilsa — yuboriladi.
    /// </summary>
    /// <remarks>
    /// Oddiy «bulutdan keldi» bayrog'i yetmasligining sababi shu: boshqa
    /// kassada yozilgan chekni bu kassa o'zgartirishi mumkin (masalan qarzni
    /// undirsa) va o'sha o'zgarish bulutga CHIQISHI shart. Belgi qatorning
    /// qaysi holati kelganini eslaydi, shuning uchun keyingi o'zgarish uni
    /// o'z-o'zidan «yangi» qiladi.
    /// </remarks>
    [Fact]
    public async Task Dokonda_ozgartirilgan_qator_yuboriladi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        var product = NewProduct("Bulutdan kelgan");
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();

        var applied = await h.Db.Products.IgnoreQueryFilters()
            .Where(x => x.Id == product.Id).Select(x => x.UpdatedAt).FirstAsync();
        h.Db.SyncedRowMarks.Add(new SyncedRowMark
        {
            RowId = product.Id, TableName = nameof(Product), AppliedUpdatedAt = applied,
        });
        await h.Db.SaveChangesAsync();

        // Do'konda narx o'zgardi — soat oldinga suriladi, ya'ni yangi
        // `UpdatedAt` belgidagidan farq qiladi.
        h.DbClock.Advance(TimeSpan.FromMinutes(5));
        var row = await h.Db.Products.IgnoreQueryFilters().FirstAsync(x => x.Id == product.Id);
        row.SalePrice = 777;
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        var result = await service.PushAsync();

        Assert.Equal(1, result.Rows);
        Assert.Equal(777, Assert.Single(handler.Sent[0].Products).SalePrice);
    }

    [Fact]
    public async Task Yuborilgan_yozuv_qayta_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        h.Db.Products.Add(NewProduct("Sement"));
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        await service.PushAsync();
        handler.Sent.Clear();
        var again = await service.PushAsync();

        Assert.Equal(0, again.Rows);
        Assert.Empty(handler.Sent);
    }

    /// <summary>
    /// ENG MUHIM. Bitta saqlashdagi yozuvlar aynan bir xil vaqt oladi
    /// (masalan Excel'dan import). Faqat vaqtga tayangan belgi joyidan
    /// qimirlamas va do'kon o'sha paketni ABADIY qayta yuboraverardi — yangi
    /// savdolar esa navbatda turib qolardi.
    /// </summary>
    [Fact]
    public async Task Bir_xil_vaqtli_katta_paket_navbatni_qotirmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);

        // 250 ta tovar — bitta saqlash, ya'ni hammasi bir xil UpdatedAt.
        // Paket chegarasi 200, demak ular ikkiga bo'linadi.
        for (var i = 0; i < 250; i++) h.Db.Products.Add(NewProduct($"Tovar {i}"));
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        var result = await service.PushAsync();

        Assert.Equal(250, result.Rows);
        Assert.Equal(2, handler.Sent.Count);          // 200 + 50
        Assert.Equal(200, handler.Sent[0].Products.Count);
        Assert.Equal(50, handler.Sent[1].Products.Count);

        // Va hech biri ikki marta yuborilmagan.
        var ids = handler.Sent.SelectMany(p => p.Products).Select(p => p.Id).ToList();
        Assert.Equal(250, ids.Distinct().Count());
    }

    /// <summary>
    /// Do'kon bir kun aloqasiz ishlagan bo'lsa, navbat bitta chaqiruvda
    /// bo'shashi kerak — besh daqiqada 200 tadan emas.
    /// </summary>
    [Fact]
    public async Task Katta_navbat_bitta_chaqiruvda_boshaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        for (var i = 0; i < 450; i++) h.Db.Products.Add(NewProduct($"Tovar {i}"));
        await h.Db.SaveChangesAsync();

        var (service, _) = NewService(h);
        var result = await service.PushAsync();

        Assert.Equal(450, result.Rows);
    }

    /// <summary>
    /// Otasi hali yetib bormagan qator KECHIKTIRILADI. Belgi surilsa, sotuv
    /// keyin yetib borar, qatori esa hech qachon — va buni hech kim sezmasdi.
    /// </summary>
    [Fact]
    public async Task Kechiktirilgan_jadval_belgisi_surilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        h.Db.Products.Add(NewProduct("Sement"));
        await h.Db.SaveChangesAsync();

        // Bulut «Product qatorlari kechiktirildi» deb javob beradi.
        var (service, handler) = NewService(h, payload => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncPushResultDto(
                0,
                new Dictionary<string, int>(),
                new Dictionary<string, int> { ["Product"] = payload.Products.Count })),
        });

        await service.PushAsync();

        // Belgi yozilmagan — ya'ni keyingi urinishda o'sha qator qaytadan
        // yuboriladi.
        Assert.Empty(await h.Db.SyncPushStates.ToListAsync());
        Assert.True(handler.Sent.Count >= 1);
    }

    [Fact]
    public async Task Aloqa_yoqida_belgi_surilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);
        h.Db.Products.Add(NewProduct("Sement"));
        await h.Db.SaveChangesAsync();

        var (service, _) = NewService(h, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var result = await service.PushAsync();

        Assert.False(result.Success);
        Assert.Empty(await h.Db.SyncPushStates.ToListAsync());
    }

    [Fact]
    public async Task Bulutdan_malumot_olmagan_dokon_yubormaydi()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.Products.Add(NewProduct("Sement"));
        await h.Db.SaveChangesAsync();     // SyncState YO'Q

        var (service, handler) = NewService(h);
        var result = await service.PushAsync();

        Assert.True(result.Success);
        Assert.Empty(handler.Sent);
    }

    /// <summary>
    /// Sotuv qatorida <c>MarketId</c> yo'q — u marketga faqat sotuvi orqali
    /// tegishli. Filtr shu bog'lanish orqali qo'yilmasa, qo'shni do'konning
    /// qatorlari ham yuborilib ketardi.
    /// </summary>
    [Fact]
    public async Task Boshqa_dokonning_sotuv_qatorlari_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await ReadyAsync(h);

        var seller = new User
        {
            Id = Guid.NewGuid(), MarketId = 9, Username = "kassir",
            FullName = "Kassir", PasswordHash = "h", Role = Role.Seller,
        };
        h.Db.Users.Add(seller);

        var mine = new Sale { Id = Guid.NewGuid(), MarketId = 9, SellerId = seller.Id, SaleNumber = 1 };
        var theirs = new Sale { Id = Guid.NewGuid(), MarketId = 7, SellerId = seller.Id, SaleNumber = 1 };
        h.Db.Sales.AddRange(mine, theirs);
        h.Db.SaleItems.AddRange(
            new SaleItem { Id = Guid.NewGuid(), SaleId = mine.Id, Quantity = 1, SalePrice = 10 },
            new SaleItem { Id = Guid.NewGuid(), SaleId = theirs.Id, Quantity = 1, SalePrice = 10 });
        await h.Db.SaveChangesAsync();

        var (service, handler) = NewService(h);
        await service.PushAsync();

        var items = handler.Sent.SelectMany(p => p.SaleItems).ToList();
        Assert.Single(items);
        Assert.Equal(mine.Id, items[0].SaleId);
    }
}
