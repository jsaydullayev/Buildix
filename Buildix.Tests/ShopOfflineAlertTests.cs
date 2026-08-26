using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// «Do'kon aloqada emas» xabari. Bu yerdagi tekshiruvlarning aksariyati
/// xabar YUBORILMASLIGI kerak bo'lgan holatlar haqida: keraksiz vahima
/// bildirishnomalarni butunlay foydasiz qilib qo'yadi — egasi ularni
/// o'chiradi va haqiqiy nosozlikni ham ko'rmaydi.
/// </summary>
public class ShopOfflineAlertTests
{
    /// <summary>Soatni boshqarish uchun: xizmat mahalliy soatga qaraydi.</summary>
    private sealed class FixedClock : ITashkentClock
    {
        private readonly DateTime _utc;
        public FixedClock(DateTime utc) => _utc = utc;

        public DateTime UtcNow => _utc;
        public DateTime NowLocal => _utc.AddHours(5);
        public DateTime TodayLocal => NowLocal.Date;
        public (DateTime UtcStart, DateTime UtcEnd) LocalDayToUtcRange(DateTime localDate)
            => (localDate.AddHours(-5), localDate.AddDays(1).AddHours(-5));
        public DateTime ToLocal(DateTime utc) => utc.AddHours(5);
    }

    /// <summary>Toshkent vaqti bilan 14:00 — ish kuni o'rtasi.</summary>
    private static readonly DateTime Midday = new(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc);

    private static (ShopOfflineAlertService Service, ITelegramNotifier Telegram) NewService(
        TestHarness h, DateTime utcNow)
    {
        var telegram = Substitute.For<ITelegramNotifier>();
        return (new ShopOfflineAlertService(
            h.Db, h.UnitOfWork, telegram, new FixedClock(utcNow),
            NullLogger<ShopOfflineAlertService>.Instance), telegram);
    }

    private static async Task<ShopTerminal> SetupAsync(
        TestHarness h,
        DateTime? lastSeen,
        bool blocked = false,
        bool active = true,
        DateTime? lastAlert = null)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(), Username = "ega", FullName = "Ega",
            PasswordHash = "h", Role = Domain.Enums.Role.Owner,
        };
        h.Db.Users.Add(owner);
        h.Db.Markets.Add(new Market
        {
            Id = 9, Name = "Taxtapul", OwnerId = owner.Id,
            IsActive = active, IsBlocked = blocked,
        });
        var terminal = new ShopTerminal
        {
            Id = Guid.NewGuid(), MarketId = 9, Name = "Server kassa",
            KeyHash = new string('a', 64),
            LastSeenAtUtc = lastSeen,
            LastOfflineAlertAtUtc = lastAlert,
        };
        h.Db.ShopTerminals.Add(terminal);
        await h.Db.SaveChangesAsync();
        return terminal;
    }

    [Fact]
    public async Task Uzoq_sukutda_egasiga_xabar_boradi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-5));
        var (service, telegram) = NewService(h, Midday);

        var count = await service.RunAsync();

        Assert.Equal(1, count);
        await telegram.Received(1).SendToOwnerAsync(9, Arg.Is<string>(m => m.Contains("Taxtapul")), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Qisqa uzilish o'z-o'zidan tuzaladi — u haqda xabar berish shovqin
    /// bo'lardi.
    /// </summary>
    [Fact]
    public async Task Qisqa_uzilish_uchun_xabar_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddMinutes(-40));
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(0, await service.RunAsync());
        await telegram.DidNotReceive().SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ENG MUHIM. Do'kon kechasi yopiladi va kompyuter o'chiriladi — bu
    /// NORMAL holat. Har kecha xabar yuborilsa, egasi bir haftada
    /// bildirishnomalarni o'chirib qo'yardi.
    /// </summary>
    [Fact]
    public async Task Kechasi_xabar_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        // Toshkent vaqti bilan 03:00.
        var night = new DateTime(2026, 8, 26, 22, 0, 0, DateTimeKind.Utc);
        await SetupAsync(h, lastSeen: night.AddHours(-8));
        var (service, telegram) = NewService(h, night);

        Assert.Equal(0, await service.RunAsync());
        await telegram.DidNotReceive().SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Tekshiruv soatiga bir marta yuriladi — takroriy xabarsiz.
    /// </summary>
    [Fact]
    public async Task Sutkada_bir_martadan_kop_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-5));
        var (service, telegram) = NewService(h, Midday);

        await service.RunAsync();
        var again = await service.RunAsync();

        Assert.Equal(0, again);
        await telegram.Received(1).SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Xabar vaqti BAZADA saqlanadi. Birinchi variantda u fon xizmatining
    /// xotirasida turardi va API har qayta ishga tushganda unutilardi — ya'ni
    /// har yangilanish egasiga o'sha xabarni qayta yuborardi.
    /// </summary>
    [Fact]
    public async Task Qayta_ishga_tushish_xabarni_takrorlamaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-5));

        var (first, _) = NewService(h, Midday);
        await first.RunAsync();

        // Yangi xizmat nusxasi = API qayta ishga tushgani bilan bir xil.
        var (second, telegram) = NewService(h, Midday.AddHours(2));
        Assert.Equal(0, await second.RunAsync());
        await telegram.DidNotReceive().SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sutkadan_keyin_qayta_eslatiladi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-5), lastAlert: Midday.AddHours(-25));
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(1, await service.RunAsync());
        await telegram.Received(1).SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Bloklangan do'konning aloqada emasligi KUTILGAN holat — bu haqda
    /// xabar berish egasini chalg'itardi.
    /// </summary>
    [Fact]
    public async Task Bloklangan_dokon_uchun_xabar_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-9), blocked: true);
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(0, await service.RunAsync());
        await telegram.DidNotReceive().SendToOwnerAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ochirilgan_dokon_uchun_xabar_yuborilmaydi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: Midday.AddHours(-9), active: false);
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(0, await service.RunAsync());
    }

    /// <summary>
    /// Hali bir marta ham aloqaga chiqmagan kompyuter — o'rnatish
    /// tugallanmagan bo'lishi mumkin va egasi buni bilishi kerak.
    /// </summary>
    [Fact]
    public async Task Hech_qachon_aloqaga_chiqmagan_kompyuter_haqida_ham_xabar_boradi()
    {
        using var h = new TestHarness(marketId: null);
        await SetupAsync(h, lastSeen: null);
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(1, await service.RunAsync());
        await telegram.Received(1).SendToOwnerAsync(
            9, Arg.Is<string>(m => m.Contains("hali bir marta")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aloqada_bolgan_dokon_tinch_qoldiriladi()
    {
        using var h = new TestHarness(marketId: null);
        var terminal = await SetupAsync(h, lastSeen: Midday.AddMinutes(-2));
        var (service, telegram) = NewService(h, Midday);

        Assert.Equal(0, await service.RunAsync());
        Assert.Null((await h.Db.ShopTerminals.IgnoreQueryFilters().SingleAsync()).LastOfflineAlertAtUtc);
    }
}
