using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace Buildix.Tests;

/// <summary>
/// Internetsiz do'konda obuna.
///
/// <para>Do'kon dasturi obunani O'Z bazasidagi muddatga qarab hisoblaydi va
/// u faqat bulutdan tortilganda yangilanadi. Internet uzilganda qiymat
/// muzlab qoladi, soat esa yuraveradi — natijada TO'LAGAN do'kon otsrochka
/// tugagach savdo qila olmasdi va bir oydan keyin ilova umuman
/// ochilmasdi.</para>
/// </summary>
public class SubscriptionClockTests
{
    private static SubscriptionClock NewClock(TestHarness h, bool shop)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Desktop:Enabled"] = shop ? "true" : "false",
            })
            .Build();
        return new SubscriptionClock(h.Db, config);
    }

    private static async Task SeedContactAsync(TestHarness h, DateTime? lastPull)
    {
        h.Db.SyncStates.Add(new SyncState { MarketId = 1, LastPulledAtUtc = lastPull });
        await h.Db.SaveChangesAsync();
    }

    /// <summary>
    /// Bulutda vaqt oddiy: u yerda ma'lumot birlamchi va tekshirishga hech
    /// narsa to'sqinlik qilmaydi.
    /// </summary>
    [Fact]
    public async Task Bulutda_haqiqiy_vaqt_ishlatiladi()
    {
        using var h = new TestHarness(marketId: null);
        await SeedContactAsync(h, DateTime.UtcNow.AddDays(-40));

        var now = await NewClock(h, shop: false).NowAsync();

        Assert.True((DateTime.UtcNow - now).TotalSeconds < 5);
    }

    /// <summary>
    /// ASOSIY tuzatish: do'konda soat oxirgi aloqada to'xtaydi.
    /// </summary>
    [Fact]
    public async Task Dokonda_soat_oxirgi_aloqada_toxtaydi()
    {
        using var h = new TestHarness(marketId: null);
        var contact = DateTime.UtcNow.AddDays(-40);
        await SeedContactAsync(h, contact);

        var now = await NewClock(h, shop: true).NowAsync();

        Assert.Equal(contact, now, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Foydalanuvchi aytgan holat: obuna tugagan, ega to'lagan, lekin do'kon
    /// internetsiz. Savdo TO'XTAMASLIGI kerak.
    /// </summary>
    [Fact]
    public async Task Tolagan_dokon_internetsiz_ham_savdo_qila_oladi()
    {
        using var h = new TestHarness(marketId: null);

        // Aloqa uzilganda obuna hali amalda edi.
        var lastContact = DateTime.UtcNow.AddDays(-40);
        await SeedContactAsync(h, lastContact);

        var market = new Market
        {
            Id = 1, Name = "Do'kon", IsActive = true,
            // Muddat aloqadan KEYIN tugagan — bulutdagi to'lov do'konga
            // yetib bormagan.
            ExpiresAt = lastContact.AddDays(1),
        };

        var asOf = await NewClock(h, shop: true).NowAsync();
        var state = market.EvaluateSubscription(asOf, graceDays: 5, fullBlockAfterDays: 30);

        Assert.Equal(SubscriptionState.Active, state);

        // Taqqoslash uchun: eski xulq (haqiqiy vaqt) do'konni bloklardi.
        var oldBehaviour = market.EvaluateSubscription(DateTime.UtcNow, 5, 30);
        Assert.Equal(SubscriptionState.Blocked, oldBehaviour);
    }

    /// <summary>
    /// Aloqa PAYTIDA allaqachon muddati o'tgan do'kon bloklangan holicha
    /// qoladi — internetni uzish jazodan qutulish yo'li bo'lmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Aloqa_paytida_muddati_otgan_dokon_bloklangan_qoladi()
    {
        using var h = new TestHarness(marketId: null);
        var lastContact = DateTime.UtcNow.AddDays(-10);
        await SeedContactAsync(h, lastContact);

        var market = new Market
        {
            Id = 1, Name = "Do'kon", IsActive = true,
            // Aloqadan ancha oldin tugagan.
            ExpiresAt = lastContact.AddDays(-40),
        };

        var asOf = await NewClock(h, shop: true).NowAsync();

        Assert.Equal(SubscriptionState.Blocked,
            market.EvaluateSubscription(asOf, graceDays: 5, fullBlockAfterDays: 30));
    }

    /// <summary>
    /// Do'kon soati oldinga ketib qolgan bo'lsa (BIOS batareyasi), kelajakdagi
    /// «aloqa» vaqti obunani vaqtidan oldin tugatib qo'ymasligi kerak.
    /// </summary>
    [Fact]
    public async Task Kelajakdagi_aloqa_vaqti_qabul_qilinmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SeedContactAsync(h, DateTime.UtcNow.AddDays(30));

        var now = await NewClock(h, shop: true).NowAsync();

        Assert.True((DateTime.UtcNow - now).TotalSeconds < 5);
    }

    /// <summary>Hali birorta tortish bo'lmagan bo'lsa — haqiqiy vaqt.</summary>
    [Fact]
    public async Task Tortish_bolmagan_dokonda_haqiqiy_vaqt()
    {
        using var h = new TestHarness(marketId: null);
        await SeedContactAsync(h, null);

        var now = await NewClock(h, shop: true).NowAsync();

        Assert.True((DateTime.UtcNow - now).TotalSeconds < 5);
    }
}
