using Buildix.Application.Services;
using Microsoft.Extensions.Configuration;
using Buildix.Domain.Entities;

namespace Buildix.Tests;

/// <summary>
/// «Bu raqamlar qachongi?» Uchta holat farqlanishi kerak va ular boshqa-boshqa
/// narsani anglatadi: bog'lanmagan, yangi, eskirgan.
/// </summary>
public class SyncFreshnessTests
{
    /// <summary>Bulutdagi ko'rinish (egasi telefonda ko'radi).</summary>
    private static SyncFreshnessService NewService(TestHarness h) => NewService(h, shop: false);

    private static SyncFreshnessService NewService(TestHarness h, bool shop)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Desktop:Enabled"] = shop ? "true" : "false",
            })
            .Build();
        return new SyncFreshnessService(h.Db, config, h.DbClock);
    }

    private static async Task<ShopTerminal> NewTerminalAsync(
        TestHarness h, int marketId = 9, DateTime? lastSeen = null, bool revoked = false,
        DateTime? lastPush = null)
    {
        var terminal = new ShopTerminal
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Name = "Server kassa",
            KeyHash = new string('a', 64),
            LastSeenAtUtc = lastSeen,
            // Eski sinovlar aloqa vaqtini berardi va yangilik o'shanga
            // qarardi. Endi yangilik MA'LUMOT KELGAN vaqtga qaraydi, shuning
            // uchun sukut bo'yicha ikkalasi bir xil — ilgarigi xulq.
            LastPushAtUtc = lastPush ?? lastSeen,
            RevokedAtUtc = revoked ? h.DbClock.GetUtcNow().UtcDateTime : null,
        };
        h.Db.ShopTerminals.Add(terminal);
        await h.Db.SaveChangesAsync();
        return terminal;
    }

    /// <summary>
    /// Bog'lanmagan do'kon «aloqa yo'q» dan BOSHQA holat: bu yerda kutish
    /// emas, o'rnatishni tugatish talab qilinadi.
    /// </summary>
    [Fact]
    public async Task Boglanmagan_dokon_alohida_korsatiladi()
    {
        using var h = new TestHarness(marketId: null);

        var status = await NewService(h).GetAsync(9);

        Assert.False(status.IsPaired);
        Assert.False(status.IsFresh);
        Assert.Null(status.LastSyncAtUtc);
    }

    [Fact]
    public async Task Yaqinda_aloqada_bolgan_dokon_yangi()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, lastSeen: h.DbClock.GetUtcNow().UtcDateTime);

        var status = await NewService(h).GetAsync(9);

        Assert.True(status.IsPaired);
        Assert.True(status.IsFresh);
        Assert.Equal(0, status.SecondsSinceSync);
        Assert.Equal("Server kassa", status.TerminalName);
    }

    /// <summary>
    /// Do'kon har besh daqiqada aloqaga chiqadi — bir-ikki o'tkazib yuborish
    /// normal va uni «aloqa yo'q» deb ko'rsatish keraksiz vahima bo'lardi.
    /// </summary>
    [Fact]
    public async Task Ozgina_kechikish_hali_eskirgan_emas()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, lastSeen: h.DbClock.GetUtcNow().UtcDateTime);

        h.DbClock.Advance(TimeSpan.FromMinutes(10));
        var status = await NewService(h).GetAsync(9);

        Assert.True(status.IsFresh);
        Assert.Equal(600, status.SecondsSinceSync);
    }

    [Fact]
    public async Task Uzoq_aloqasizlik_eskirgan_deb_belgilanadi()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, lastSeen: h.DbClock.GetUtcNow().UtcDateTime);

        h.DbClock.Advance(TimeSpan.FromHours(2));
        var status = await NewService(h).GetAsync(9);

        Assert.True(status.IsPaired);
        Assert.False(status.IsFresh);
        Assert.Equal(7200, status.SecondsSinceSync);
    }

    /// <summary>
    /// Bekor qilingan kompyuter bog'langan hisoblanmaydi: uning oxirgi aloqa
    /// vaqti hech narsani anglatmaydi va uni ko'rsatish egasini adashtirardi.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_kompyuter_hisobga_olinmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, lastSeen: h.DbClock.GetUtcNow().UtcDateTime, revoked: true);

        var status = await NewService(h).GetAsync(9);

        Assert.False(status.IsPaired);
    }

    [Fact]
    public async Task Boshqa_dokonning_holati_korinmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, marketId: 7, lastSeen: h.DbClock.GetUtcNow().UtcDateTime);

        var status = await NewService(h).GetAsync(9);

        Assert.False(status.IsPaired);
    }

    /// <summary>
    /// Bog'langan, lekin hali bir marta ham aloqaga chiqmagan holat ham
    /// bo'lishi mumkin — u «eskirgan» deb ko'rsatiladi, chunki raqamlar
    /// haqiqatan yo'q.
    /// </summary>
    [Fact]
    public async Task Hali_aloqaga_chiqmagan_kompyuter_eskirgan()
    {
        using var h = new TestHarness(marketId: null);
        await NewTerminalAsync(h, lastSeen: null);

        var status = await NewService(h).GetAsync(9);

        Assert.True(status.IsPaired);
        Assert.False(status.IsFresh);
        Assert.Null(status.LastSyncAtUtc);
    }

    // ── «Aloqa bor, lekin ma'lumot kelmayapti» ────────────────────────────
    // Eng yashirin nosozlik. Do'kon har daqiqada bulutga murojaat qiladi va
    // kalit tekshiruvidan o'tadi, lekin yuborish tashqi kalit xatosi bilan
    // yiqiladi. Ilgari yangilik ALOQA vaqtiga qarardi, shuning uchun ekranda
    // yashil «hozirgina sinxron» turardi — bulutga esa haftalab birorta savdo
    // tushmasdi.

    /// <summary>
    /// Aloqa yangi, lekin ma'lumot eski — bu «hammasi joyida» EMAS.
    /// </summary>
    [Fact]
    public async Task Aloqa_bor_lekin_malumot_kelmasa_yangi_deb_korsatilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var now = h.DbClock.GetUtcNow().UtcDateTime;
        await NewTerminalAsync(h, lastSeen: now, lastPush: now.AddHours(-5));

        var status = await NewService(h).GetAsync(9);

        Assert.True(status.IsPaired);
        Assert.False(status.IsFresh);
        Assert.NotNull(status.Error);
    }

    /// <summary>
    /// Do'kon ham aloqada emas, ma'lumot ham eski — bu oddiy «aloqa yo'q»
    /// holati va uni buzilgan sinxronizatsiya deb ko'rsatmaslik kerak.
    /// </summary>
    [Fact]
    public async Task Dokon_umuman_aloqada_bolmasa_bu_buzilish_emas()
    {
        using var h = new TestHarness(marketId: null);
        var old = h.DbClock.GetUtcNow().UtcDateTime.AddHours(-5);
        await NewTerminalAsync(h, lastSeen: old, lastPush: old);

        var status = await NewService(h).GetAsync(9);

        Assert.False(status.IsFresh);
        Assert.Null(status.Error);
    }

    // ── Do'kon kompyuteridagi ko'rinish ───────────────────────────────────
    // Do'konda ma'lumot bazaning o'zida turadi va u har doim jonli. Ilgari
    // bu ekran ham bulutdagi kabi ShopTerminals jadvalini o'qirdi — u esa
    // do'kon bazasida HECH QACHON to'ldirilmaydi, natijada kassirning har
    // ekranida doimiy qizil «bog'lanmagan» chizig'i turardi.

    /// <summary>
    /// Do'konda terminal jadvali bo'sh bo'lsa ham, sinxronizatsiya holati
    /// bor ekan — «bog'lanmagan» deb ko'rsatilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Dokonda_bosh_terminal_jadvali_boglanmagan_degani_emas()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState
        {
            MarketId = 9,
            LastPushedAtUtc = h.DbClock.GetUtcNow().UtcDateTime,
        });
        await h.Db.SaveChangesAsync();

        var status = await NewService(h, shop: true).GetAsync(9);

        Assert.True(status.IsPaired);
        Assert.True(status.IsFresh);
        Assert.True(status.IsShopMachine);
    }

    /// <summary>Do'konda yuborish xatosi bo'lsa — u ekranda ko'rinishi kerak.</summary>
    [Fact]
    public async Task Dokonda_yuborish_xatosi_korinadi()
    {
        using var h = new TestHarness(marketId: null);
        h.Db.SyncStates.Add(new SyncState
        {
            MarketId = 9,
            LastPushedAtUtc = h.DbClock.GetUtcNow().UtcDateTime,
            LastPushError = "Bulut javobi: 500",
        });
        await h.Db.SaveChangesAsync();

        var status = await NewService(h, shop: true).GetAsync(9);

        // Oxirgi muvaffaqiyat hozirgina bo'lsa ham, xato borligi «yangi»
        // deyishga yo'l qo'ymaydi — aynan shu holatda navbat to'xtab qoladi.
        Assert.False(status.IsFresh);
        Assert.Equal("Bulut javobi: 500", status.Error);
    }

    /// <summary>Hali bulutdan hech narsa tortilmagan do'kon — bog'lanmagan.</summary>
    [Fact]
    public async Task Dokonda_holat_yoq_bolsa_boglanmagan()
    {
        using var h = new TestHarness(marketId: null);

        var status = await NewService(h, shop: true).GetAsync(9);

        Assert.False(status.IsPaired);
    }
}
