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

    /// <summary>
    /// Bitta do'kon — bitta baza. Ikkita bog'langan kompyuter bo'lsa, bitta
    /// do'kon nomidan ikkita mustaqil baza ish ko'radi: ikkalasi ham o'z chek
    /// raqamlarini beradi va bulutga bir-birining ustiga yozadi. Bu pul
    /// ma'lumotini JIMGINA buzadi.
    /// </summary>
    [Fact]
    public async Task Ikkinchi_kompyuter_boglanmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var first = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        await service.RedeemAsync(first.Value.Code, "Server kassa", null);

        var second = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var blocked = await service.RedeemAsync(second.Value.Code, "Ikkinchi kompyuter", null);

        Assert.True(blocked.IsFailure);
        Assert.Contains("Server kassa", blocked.Error);
        Assert.Single(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Kompyuter almashtirilganda: eskisi bekor qilinadi va yangisi
    /// bog'lanadi. Busiz yuqoridagi qoida boshi berk ko'chaga olib borardi.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingandan_keyin_yangisi_boglanadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var first = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var old = await service.RedeemAsync(first.Value.Code, "Eski kompyuter", null);

        var revoked = await service.RevokeAsync(old.Value.TerminalId, Guid.NewGuid());
        Assert.True(revoked.IsSuccess);

        var second = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var fresh = await service.RedeemAsync(second.Value.Code, "Yangi kompyuter", null);

        Assert.True(fresh.IsSuccess, fresh.Error);
        // Eski kalit endi ishlamaydi.
        Assert.Null(await service.AuthenticateAsync(old.Value.Key));
        Assert.NotNull(await service.AuthenticateAsync(fresh.Value.Key));
    }

    [Fact]
    public async Task Ochirilgan_dokonga_boglanib_bolmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());

        market.IsActive = false;          // egasi o'chirildi
        await h.Db.SaveChangesAsync();

        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);

        Assert.True(paired.IsFailure);
        Assert.Empty(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Aloqa vaqti — «do'kon uch kundan beri chiqmayapti» degan xabarning
    /// yagona manbai. U yangilanmasa, xabar hech qachon to'g'ri bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Aloqa_vaqti_yangilanadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);

        var terminal = (await service.AuthenticateAsync(paired.Value.Key))!;
        var pairedAt = terminal.LastSeenAtUtc;

        h.DbClock.Advance(TimeSpan.FromHours(6));
        await service.TouchAsync(terminal, "192.168.1.44");

        var after = await h.Db.ShopTerminals.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(pairedAt!.Value.AddHours(6), after.LastSeenAtUtc);
        Assert.Equal("192.168.1.44", after.LastIpAddress);
    }

    /// <summary>
    /// Sinxronizatsiya tez-tez takrorlanadi — har chaqiruvda yozish bazani
    /// keraksiz yuklardi.
    /// </summary>
    [Fact]
    public async Task Aloqa_vaqti_har_soniyada_yozilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Kassa", null);
        var terminal = (await service.AuthenticateAsync(paired.Value.Key))!;
        var before = terminal.LastSeenAtUtc;

        h.DbClock.Advance(TimeSpan.FromSeconds(20));
        await service.TouchAsync(terminal, null);

        Assert.Equal(before, terminal.LastSeenAtUtc);
    }

    [Fact]
    public async Task Royxatda_bekor_qilinganlar_ham_korinadi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Eski", null);
        await service.RevokeAsync(paired.Value.TerminalId, Guid.NewGuid());

        var list = await service.ListAsync(market.Id);

        var row = Assert.Single(list);
        Assert.Equal("Eski", row.Name);
        Assert.NotNull(row.RevokedAtUtc);
    }

    [Fact]
    public async Task Mavjud_bolmagan_dokon_uchun_kod_berilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var service = NewService(h);

        var issued = await service.IssueCodeAsync(999, Guid.NewGuid());

        Assert.True(issued.IsFailure);
    }

    // ── Kodsiz faollashtirish (do'kon egasining login-paroli) ──────────────
    // Parolni tekshirish kontrollerda, shu sababli bu yerdagi sinovlar
    // faollashtirishning O'ZI kod oqimi bilan bir xil chegaralarga
    // bo'ysunishini tekshiradi.

    [Fact]
    public async Task Kodsiz_faollashtirilganda_kalit_beriladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var paired = await service.ActivateAsync(market.Id, "Server kassa", "192.168.1.10");

        Assert.True(paired.IsSuccess, paired.Error);
        Assert.Equal(market.Id, paired.Value.MarketId);
        Assert.Equal("Sement Savdo", paired.Value.MarketName);
        Assert.False(string.IsNullOrWhiteSpace(paired.Value.Key));

        // Kalit bazada OCHIQ yotmasligi kerak — faqat hash'i.
        var stored = Assert.Single(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
        Assert.DoesNotContain(paired.Value.Key, stored.KeyHash);
        Assert.Equal("192.168.1.10", stored.LastIpAddress);
    }

    [Fact]
    public async Task Kodsiz_berilgan_kalit_bilan_kompyuter_tanaladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);

        var paired = await service.ActivateAsync(market.Id, "Kassa", null);
        var terminal = await service.AuthenticateAsync(paired.Value.Key);

        Assert.NotNull(terminal);
        Assert.Equal(market.Id, terminal!.MarketId);
    }

    /// <summary>
    /// Bitta do'kon — bitta baza. Bu chegara kod oqimida bor edi; kodsiz yo'l
    /// uni CHETLAB O'TMASLIGI kerak, aks holda egasi bilmasdan ikkinchi
    /// mustaqil baza yaratib qo'yardi va ikkalasi bulutda bir-birining
    /// ustiga yozardi.
    /// </summary>
    [Fact]
    public async Task Kodsiz_yol_ikkinchi_kompyuterni_qoshmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        await service.ActivateAsync(market.Id, "Birinchi", null);

        var second = await service.ActivateAsync(market.Id, "Ikkinchi", null);

        Assert.True(second.IsFailure);
        Assert.Equal("ALREADY_PAIRED", second.Code);
        Assert.Single(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Kodsiz_yol_ochirilgan_dokonga_boglamaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        market.IsActive = false;
        await h.Db.SaveChangesAsync();
        var service = NewService(h);

        var paired = await service.ActivateAsync(market.Id, "Kassa", null);

        Assert.True(paired.IsFailure);
        Assert.Empty(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Kodsiz_yol_mavjud_bolmagan_dokonga_boglamaydi()
    {
        using var h = new TestHarness(marketId: null);
        var service = NewService(h);

        var paired = await service.ActivateAsync(999, "Kassa", null);

        Assert.True(paired.IsFailure);
        Assert.Empty(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Eski kompyuter bekor qilingandan keyin kodsiz yo'l yana ishlashi
    /// kerak — kassa almashtirilganda odatiy holat aynan shu.
    /// </summary>
    [Fact]
    public async Task Eskisi_bekor_qilingach_kodsiz_yol_yana_ishlaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var first = await service.ActivateAsync(market.Id, "Eski", null);
        await service.RevokeAsync(first.Value.TerminalId, Guid.NewGuid());

        var second = await service.ActivateAsync(market.Id, "Yangi", null);

        Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.Value.Key, second.Value.Key);
        // Eski kalit endi o'tmaydi.
        Assert.Null(await service.AuthenticateAsync(first.Value.Key));
    }

    /// <summary>
    /// Qayta o'rnatilgan SERVER kompyuter o'zini qaytadan bog'lay oladi.
    /// </summary>
    /// <remarks>
    /// <para>Eng og'ir holat shu edi. Server kompyuter qayta o'rnatilsa
    /// (disk tozalandi, Windows qaytadan qo'yildi) yangi nusxada kalit
    /// qolmaydi, bulutda esa ESKI yozuv tirik turadi. Bog'lash «allaqachon
    /// bog'langan» deb rad etilar, bekor qilinishi kerak bo'lgan kompyuter
    /// esa endi mavjud emas edi — ya'ni do'kon o'z bazasiga qaytolmasdi va
    /// butunlay qulflanib qolardi.</para>
    ///
    /// <para>Egalik isbotlangan (bu yo'lga faqat egasining login-paroli
    /// bilan kelinadi), shuning uchun u eskisini shu yerdan bekor qila
    /// oladi.</para>
    /// </remarks>
    [Fact]
    public async Task Qayta_ornatilgan_server_ozini_qaytadan_boglay_oladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var before = await service.ActivateAsync(market.Id, "Server kassa", null);

        // Kompyuter qayta o'rnatildi — kalit yo'qoldi, lekin bulutda yozuv bor.
        var blocked = await service.ActivateAsync(market.Id, "Server kassa", null);
        Assert.True(blocked.IsFailure);
        Assert.Equal("ALREADY_PAIRED", blocked.Code);

        // Egasi bandni belgilaydi.
        var after = await service.ActivateAsync(
            market.Id, "Server kassa", null, replaceExisting: true);

        Assert.True(after.IsSuccess, after.Error);
        Assert.NotEqual(before.Value.Key, after.Value.Key);
        // Eski kalit o'ldi, yangisi ishlaydi.
        Assert.Null(await service.AuthenticateAsync(before.Value.Key));
        Assert.NotNull(await service.AuthenticateAsync(after.Value.Key));
        // Eskisi yo'qolmaydi — «qachon almashtirilgan» izi qoladi.
        var rows = await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, t => t.RevokedAtUtc is null);
    }

    /// <summary>
    /// KOD yo'lida eskisining o'rnini egallash yo'q.
    /// </summary>
    /// <remarks>
    /// Kodni taxmin qilgan odam do'konning kassasini bulutdan uzib, o'rniga
    /// o'zinikini qo'ya olardi. Almashtirish faqat egasining login-paroli
    /// bilan bo'ladi.
    /// </remarks>
    [Fact]
    public async Task Kod_yoli_eskisini_almashtira_olmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        await service.ActivateAsync(market.Id, "Server kassa", null);

        var issued = await service.IssueCodeAsync(market.Id, Guid.NewGuid());
        var paired = await service.RedeemAsync(issued.Value.Code, "Begona", null);

        Assert.True(paired.IsFailure);
        Assert.Equal("ALREADY_PAIRED", paired.Code);
        Assert.Single(await h.Db.ShopTerminals.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Do'kon egasi O'Z kompyuterini bekor qila oladi.
    /// </summary>
    /// <remarks>
    /// Ilgari buni faqat SuperAdmin qila olardi va unga ham interfeys yo'q
    /// edi — ya'ni bog'lash oynasidagi «panelda eskisini bekor qiling» degan
    /// xabar mavjud bo'lmagan joyga yo'naltirardi va egasi server
    /// kompyuterini almashtira olmasdi.
    /// </remarks>
    [Fact]
    public async Task Egasi_oz_kompyuterini_bekor_qila_oladi()
    {
        using var h = new TestHarness(marketId: null);
        var market = await NewMarketAsync(h);
        var service = NewService(h);
        var paired = await service.ActivateAsync(market.Id, "Server kassa", null);

        var revoked = await service.RevokeAsync(
            paired.Value.TerminalId, Guid.NewGuid(), requireMarketId: market.Id);

        Assert.True(revoked.IsSuccess, revoked.Error);
        Assert.Null(await service.AuthenticateAsync(paired.Value.Key));
    }

    /// <summary>
    /// Egasi BOSHQA do'konning kompyuterini uza olmaydi.
    /// </summary>
    /// <remarks>
    /// Kompyuter <c>IgnoreQueryFilters()</c> bilan, ID bo'yicha izlanadi —
    /// ya'ni tenant filtri o'chirilgan joyda EF hech narsani himoya qilmaydi.
    /// Tekshiruvsiz ega qo'shni do'konning terminal ID sini yuborib, uning
    /// kassasini bulutdan uzib qo'ya olardi.
    /// </remarks>
    [Fact]
    public async Task Begona_dokonning_kompyuterini_uzib_bolmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var mine = await NewMarketAsync(h, id: 5, name: "Mening do'konim");
        var other = await NewMarketAsync(h, id: 7, name: "Qo'shni do'kon");
        var service = NewService(h);

        var foreign = await service.ActivateAsync(other.Id, "Qo'shnining kassasi", null);

        var revoked = await service.RevokeAsync(
            foreign.Value.TerminalId, Guid.NewGuid(), requireMarketId: mine.Id);

        Assert.True(revoked.IsFailure);
        Assert.Equal("NOT_FOUND", revoked.Code);
        // Qo'shnining kassasi ishlab turibdi.
        Assert.NotNull(await service.AuthenticateAsync(foreign.Value.Key));
    }
}
