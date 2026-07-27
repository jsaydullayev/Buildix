using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// S6 — obuna bildirishnomalari (Telegram; SMS ishlatilmaydi).
///
/// <para>Ikki narsa muhim: eslatma bir davrga BIR MARTA ketishi va
/// yetib bormagani JIMGINA yo'qolmasligi (operator qo'ng'iroq qilishi uchun
/// «unreachable» sanaladi).</para>
/// </summary>
public class SubscriptionNotifierTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock : ITashkentClock
    {
        public DateTime UtcNow => Now;
        public DateTime NowLocal => Now.AddHours(5);
        public DateTime TodayLocal => new(2026, 8, 10);
        public (DateTime UtcStart, DateTime UtcEnd) LocalDayToUtcRange(DateTime d)
            => (DateTime.SpecifyKind(d.Date.AddHours(-5), DateTimeKind.Utc),
                DateTime.SpecifyKind(d.Date.AddHours(19), DateTimeKind.Utc));
        public DateTime ToLocal(DateTime utc) => utc.AddHours(5);
    }

    private static (SubscriptionNotifierService Service, TestHarness H, ITelegramNotifier Tg) NewService(
        bool notifyExpiring = true)
    {
        var h = new TestHarness(marketId: null);
        var tg = Substitute.For<ITelegramNotifier>();
        // Default: yuborish muvaffaqiyatli.
        tg.SendToChatAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var settings = notifyExpiring
            ? FixedPlatformSettings.Default
            : FixedPlatformSettings.WithNotifications(expiring: false);

        return (new SubscriptionNotifierService(
            h.Db, tg, settings, new FixedClock(),
            NullLogger<SubscriptionNotifierService>.Instance), h, tg);
    }

    private static Market SeedMarket(TestHarness h, int id, DateTime? expiresAt, long? ownerChatId)
    {
        var ownerId = Guid.NewGuid();
        h.Db.Users.Add(new User
        {
            Id = ownerId,
            FullName = $"Ega {id}",
            Username = $"ega{id}",
            PasswordHash = "x",
            Role = Role.Owner,
            MarketId = id,
            IsActive = true,
            TelegramChatId = ownerChatId,
        });
        var m = new Market
        {
            Id = id, Name = $"Do'kon {id}", IsActive = true,
            ExpiresAt = expiresAt, OwnerId = ownerId,
        };
        h.Db.Markets.Add(m);
        h.Db.SaveChanges();
        return m;
    }

    [Fact]
    public async Task A_reminder_goes_out_once_per_subscription_period()
    {
        var (service, h, tg) = NewService();
        SeedMarket(h, 1, Now.AddDays(2), ownerChatId: 111);

        var first = await service.RemindExpiringAsync();
        Assert.Equal(1, first.Sent);

        // Ikkinchi o'tish — stamp o'sha muddat uchun qo'yilgan, takror ketmaydi.
        var second = await service.RemindExpiringAsync();
        Assert.Equal(0, second.Sent);
        await tg.Received(1).SendToChatAsync(111, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paying_for_a_new_period_re_arms_the_reminder()
    {
        var (service, h, _) = NewService();
        var m = SeedMarket(h, 1, Now.AddDays(2), ownerChatId: 111);
        await service.RemindExpiringAsync();

        // To'lov muddatni oldinga surdi — yangi davr, eski stamp endi mos
        // kelmaydi. Muddat uzoq ekan, eslatma hali ketmaydi.
        h.Db.Markets.Single(x => x.Id == m.Id).ExpiresAt = Now.AddDays(32);
        h.Db.SaveChanges();
        h.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await service.RemindExpiringAsync()).Sent);

        // Yangi davrning oxiri yaqinlashdi (boshqa sana!) — eslatma yana
        // ishlaydi. Alohida «yuborildimi» bayrog'i kerak emas: stamp sifatida
        // muddatning O'ZI ishlatilgani shuni bepul beradi.
        h.Db.Markets.Single(x => x.Id == m.Id).ExpiresAt = Now.AddDays(3);
        h.Db.SaveChanges();
        h.Db.ChangeTracker.Clear();
        Assert.Equal(1, (await service.RemindExpiringAsync()).Sent);
    }

    [Fact]
    public async Task A_failed_send_is_never_stamped_as_done()
    {
        var (service, h, tg) = NewService();
        SeedMarket(h, 1, Now.AddDays(2), ownerChatId: 111);
        tg.SendToChatAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var result = await service.RemindExpiringAsync();

        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Unreachable);
        // Stamp qo'yilmagan — keyingi o'tishda yana urinadi. Aks holda bitta
        // jim xatolik butun davr eslatmasini yo'q qilardi.
        Assert.Null(h.Db.Markets.Single(m => m.Id == 1).RenewalReminderSentFor);
    }

    [Fact]
    public async Task An_owner_without_telegram_is_counted_not_ignored()
    {
        var (service, h, tg) = NewService();
        SeedMarket(h, 1, Now.AddDays(2), ownerChatId: null);

        var result = await service.RemindExpiringAsync();

        Assert.Equal(0, result.Sent);
        // Operator uni qo'ng'iroq bilan xabardor qilishi kerak — shuning uchun
        // son qaytariladi va ro'yxatda «Telegram yo'q» belgisi chiqadi.
        Assert.Equal(1, result.Unreachable);
        await tg.DidNotReceive().SendToChatAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_operator_switched_reminders_off()
    {
        var (service, h, tg) = NewService(notifyExpiring: false);
        SeedMarket(h, 1, Now.AddDays(2), ownerChatId: 111);

        Assert.Equal(0, (await service.RemindExpiringAsync()).Sent);
        await tg.DidNotReceive().SendToChatAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_stores_inside_the_reminder_window_are_touched()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, Now.AddDays(2), ownerChatId: 111);   // ✔ 3 kun ichida
        SeedMarket(h, 2, Now.AddDays(10), ownerChatId: 222);  // ✗ hali erta
        SeedMarket(h, 3, Now.AddDays(-1), ownerChatId: 333);  // ✗ allaqachon o'tgan
        SeedMarket(h, 4, null, ownerChatId: 444);             // ✗ muddatsiz

        Assert.Equal(1, (await service.RemindExpiringAsync()).Sent);
    }

    [Fact]
    public async Task Overdue_reminders_cover_every_late_store()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, Now.AddDays(-2), ownerChatId: 111);  // ✔ kechikkan
        SeedMarket(h, 2, Now.AddDays(-20), ownerChatId: null); // ✔ lekin Telegramsiz
        SeedMarket(h, 3, Now.AddDays(5), ownerChatId: 333);   // ✗ hali to'langan

        var result = await service.RemindOverdueAsync();

        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Unreachable);
    }
}
