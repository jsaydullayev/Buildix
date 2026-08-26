using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buildix.Tests;

/// <summary>
/// Do'konni bulutga bog'lash. Bu yerdagi tekshiruvlar xavfsizlik
/// chegarasida turadi: kod bir marta ishlashi, bekor qilingan kalit
/// o'tmasligi va kalitning bazada ochiq yotmasligi.
/// </summary>
public class TerminalPairingTests
{
    private static TerminalPairingService NewService(TestHarness h) =>
        new(h.Db, h.UnitOfWork, NullLogger<TerminalPairingService>.Instance, h.DbClock);

    private static async Task<Market> NewMarketAsync(TestHarness h, int id = 5, string name = "Sement Savdo")
    {
        var market = new Market { Id = id, Name = name };
        h.Db.Markets.Add(market);
        await h.Db.SaveChangesAsync();
        return market;
    }

    [Fact]
    public async Task Kod_berilib_kalitga_almashadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        Assert.True(issued.IsSuccess, issued.Error);
        Assert.StartsWith("BX-", issued.Value.Code);

        var paired = await service.RedeemAsync(issued.Value.Code, "Server kassa", "192.168.1.10");

        Assert.True(paired.IsSuccess, paired.Error);
        Assert.Equal(market.Id, paired.Value.MarketId);
        Assert.Equal("Sement Savdo", paired.Value.MarketName);
        Assert.False(string.IsNullOrWhiteSpace(paired.Value.Key));
    }

    /// <summary>
    /// Kod texnik uni QANDAY yozsa ham ishlashi kerak: kichik harflar,
    /// chiziqchasiz, bo'shliqlar bilan. Birinchi variantda «BX» prefiksining
    /// harflari tozalashdan o'tib ketib, kod umuman tanilmasdi.
    /// </summary>
    [Theory]
    [InlineData("{0}")]
    [InlineData(" {0} ")]
    public async Task Kod_yozilishidan_qati_nazar_ishlaydi(string format)
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        var typed = string.Format(format, issued.Value.Code);
        var paired = await service.RedeemAsync(typed, "Kassa", null);

        Assert.True(paired.IsSuccess, $"«{typed}» qabul qilinmadi: {paired.Error}");
    }

    [Fact]
    public async Task Kichik_harf_va_chiziqchasiz_ham_ishlaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        var typed = issued.Value.Code.Replace("-", "").ToLowerInvariant();
        var paired = await service.RedeemAsync(typed, "Kassa", null);

        Assert.True(paired.IsSuccess, $"«{typed}» qabul qilinmadi: {paired.Error}");
    }

    [Fact]
    public async Task Kod_ikkinchi_marta_ishlamaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        var first = await service.RedeemAsync(issued.Value.Code, "1-kassa", null);
        var second = await service.RedeemAsync(issued.Value.Code, "2-kassa", null);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Single(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Muddati_otgan_kod_ishlamaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        h.DbClock.Advance(TimeSpan.FromHours(25));
        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);

        Assert.True(paired.IsFailure);
    }

    /// <summary>
    /// Yangi kod olinganda eskisi o'lishi kerak: ikkita amaldagi kod bo'lsa,
    /// qaysi biri berilganini hech kim eslay olmaydi va eskisi qog'ozda
    /// qolib ketardi.
    /// </summary>
    [Fact]
    public async Task Yangi_kod_eskisini_bekor_qiladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var first = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var second = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        Assert.True((await service.RedeemAsync(first.Value.Code, "Kassa", null)).IsFailure);
        Assert.True((await service.RedeemAsync(second.Value.Code, "Kassa", null)).IsSuccess);
    }

    /// <summary>
    /// Kalit bazada OCHIQ yotmasligi kerak. Bulut bazasiga kirgan odam ham
    /// do'kon nomidan gapira olmasligi shart.
    /// </summary>
    [Fact]
    public async Task Kalit_bazada_ochiq_saqlanmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);
        var stored = await h.Db.ShopTerminals.IgnoreQueryFilters().FirstAsync();

        Assert.DoesNotContain(paired.Value.Key, stored.KeyHash);
        Assert.Equal(64, stored.KeyHash.Length);   // SHA-256 hex
    }

    [Fact]
    public async Task Kalit_bilan_kompyuter_taniladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Server kassa", null);

        var found = await service.AuthenticateAsync(paired.Value.Key);

        Assert.NotNull(found);
        Assert.Equal(paired.Value.TerminalId, found!.Id);
        Assert.Equal(market.Id, found.MarketId);
    }

    [Fact]
    public async Task Notogri_kalit_tanilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        await service.RedeemAsync(issued.Value.Code, "Kassa", null);

        Assert.Null(await service.AuthenticateAsync("boshqa-kalit"));
        Assert.Null(await service.AuthenticateAsync(""));
    }

    /// <summary>
    /// Bekor qilingan kalit — o'g'irlangan yoki almashtirilgan kompyuter.
    /// U bulutga umuman kira olmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_kalit_otmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);

        var terminal = await h.Db.ShopTerminals.IgnoreQueryFilters().FirstAsync();
        terminal.RevokedAtUtc = h.DbClock.GetUtcNow().UtcDateTime;
        await h.Db.SaveChangesAsync();

        Assert.Null(await service.AuthenticateAsync(paired.Value.Key));
    }

    [Fact]
    public async Task Mavjud_bolmagan_dokon_uchun_kod_berilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var service = NewService(h);

        var issued = await service.IssueCodeAsync(999, Guid.NewGuid());

        Assert.True(issued.IsFailure);
    }
}
