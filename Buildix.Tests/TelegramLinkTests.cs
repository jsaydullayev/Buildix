using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buildix.Tests;

/// <summary>
/// Telegram bog'lanishini tasdiqlash — bir martalik kod.
///
/// Asosiy invariant: chat ID foydalanuvchidan HECH QACHON so'ralmaydi, u faqat
/// botning kodidan olinadi. Ilgari xom ID yozish mumkin edi va egalikni hech
/// narsa tekshirmasdi — ya'ni begona Telegram akkauntga do'kon ma'lumotini
/// yo'naltirib qo'yish mumkin edi.
/// </summary>
public class TelegramLinkTests
{
    private const long Chat = 123_456_789L;

    private static TelegramLinkService NewService(TestHarness h) =>
        new(h.Db, h.Clock, NullLogger<TelegramLinkService>.Instance);

    private static User AddUser(TestHarness h, int marketId = 1)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = "Kassir",
            Username = $"u{Guid.NewGuid():N}"[..12],
            PasswordHash = "x",
            Role = Role.Seller,
            MarketId = marketId,
        };
        h.Db.Users.Add(user);
        h.Db.SaveChanges();
        return user;
    }

    [Fact]
    public async Task Code_is_six_digits_and_stable_within_its_window()
    {
        using var h = new TestHarness();
        var svc = NewService(h);

        var first = await svc.IssueCodeAsync(Chat);
        var second = await svc.IssueCodeAsync(Chat);

        Assert.Equal(6, first.Code.Length);
        Assert.All(first.Code, c => Assert.True(char.IsAsciiDigit(c)));
        // Har xabarga yangi kod berilsa, foydalanuvchi eskisini kiritib
        // "kod noto'g'ri" olardi — ayni bitta kod qaytishi shart.
        Assert.Equal(first.Code, second.Code);
        Assert.Single(h.Db.TelegramLinkCodes);
    }

    [Fact]
    public async Task Redeeming_a_code_yields_the_chat_that_received_it()
    {
        using var h = new TestHarness();
        var svc = NewService(h);
        var user = AddUser(h);

        var (code, _) = await svc.IssueCodeAsync(Chat);
        var chatId = await svc.ConsumeAsync(user, code);

        Assert.Equal(Chat, chatId);

        // ConsumeAsync ATAYLAB saqlamaydi — chaqiruvchi (UserService) uni
        // foydalanuvchi qatori bilan bitta tranzaksiyada yozadi. Shu yerda
        // o'sha saqlashni taqlid qilamiz.
        user.TelegramChatId = chatId;
        h.Db.SaveChanges();

        // Ikkinchi marta ishlamaydi — kod bir martalik.
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(user, code));
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        using var h = new TestHarness();
        var svc = NewService(h);
        var user = AddUser(h);

        var (code, _) = await svc.IssueCodeAsync(Chat);
        // Muddatni o'tkazamiz — soat testda o'zgarmas, shuning uchun qatorni
        // to'g'ridan-to'g'ri eskirtiramiz.
        var row = h.Db.TelegramLinkCodes.Single();
        row.ExpiresAtUtc = h.Clock.UtcNow.AddMinutes(-1);
        h.Db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(user, code));
    }

    [Fact]
    public async Task Code_for_a_chat_already_linked_elsewhere_is_refused()
    {
        using var h = new TestHarness();
        var svc = NewService(h);
        var owner = AddUser(h);
        owner.TelegramChatId = Chat;
        h.Db.SaveChanges();

        var other = AddUser(h);
        var (code, _) = await svc.IssueCodeAsync(Chat);

        // Global unikal indeks buni DB'da ham to'xtatadi; bu yerda foydalanuvchi
        // 500 emas, tushunarli xabar oladi.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(other, code));
        Assert.Contains("boshqa foydalanuvchiga", ex.Message);
    }

    [Fact]
    public async Task Wrong_codes_are_throttled_after_five_attempts()
    {
        using var h = new TestHarness();
        var svc = NewService(h);
        var user = AddUser(h);

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(user, "000000"));

        Assert.Equal(5, user.TelegramLinkAttempts);
        // Oltinchisi — endi kod to'g'ri bo'lsa ham o'tmaydi.
        var (code, _) = await svc.IssueCodeAsync(Chat);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsumeAsync(user, code));
        Assert.Contains("urinish", ex.Message);
    }
}
