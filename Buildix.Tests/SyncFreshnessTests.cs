using Buildix.Application.Services;
using Buildix.Domain.Entities;

namespace Buildix.Tests;

/// <summary>
/// «Bu raqamlar qachongi?» Uchta holat farqlanishi kerak va ular boshqa-boshqa
/// narsani anglatadi: bog'lanmagan, yangi, eskirgan.
/// </summary>
public class SyncFreshnessTests
{
    private static SyncFreshnessService NewService(TestHarness h) => new(h.Db, h.DbClock);

    private static async Task<ShopTerminal> NewTerminalAsync(
        TestHarness h, int marketId = 9, DateTime? lastSeen = null, bool revoked = false)
    {
        var terminal = new ShopTerminal
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Name = "Server kassa",
            KeyHash = new string('a', 64),
            LastSeenAtUtc = lastSeen,
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
}
