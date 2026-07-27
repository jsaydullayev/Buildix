using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using NSubstitute;
using Buildix.Application.Interfaces;

namespace Buildix.Tests;

/// <summary>
/// S4 — «Пользователи платформы». Bu ekrandan parol tiklanadi va hisob
/// bloklanadi, ya'ni har ikkala amal ham XAVFSIZLIK amali: DB'dagi qatorni
/// o'zgartirishning o'zi yetarli emas, mavjud sessiyalar ham uzilishi shart.
/// </summary>
public class SuperAdminUserTests
{
    private static (SuperAdminUserService Service, TestHarness H, IUserTokenEpochStore Epoch) NewService()
    {
        var h = new TestHarness(marketId: null);
        var epoch = Substitute.For<IUserTokenEpochStore>();
        return (new SuperAdminUserService(h.Db, h.UnitOfWork, epoch, h.Audit), h, epoch);
    }

    private static User SeedUser(
        TestHarness h, string name, string username, Role role, int? marketId,
        bool active = true, bool deleted = false)
    {
        var u = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("EskiParol1!"),
            Role = role,
            MarketId = marketId,
            IsActive = active,
            IsDeleted = deleted,
        };
        h.Db.Users.Add(u);
        h.Db.SaveChanges();
        return u;
    }

    private static void SeedMarket(TestHarness h, int id, string name)
    {
        h.Db.Markets.Add(new Market { Id = id, Name = name, IsActive = true, OwnerId = Guid.NewGuid() });
        h.Db.SaveChanges();
    }

    [Fact]
    public async Task The_list_covers_every_store_but_never_the_platform_admin()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, "Birinchi");
        SeedMarket(h, 2, "Ikkinchi");
        SeedUser(h, "Ega", "ega1", Role.Owner, 1);
        SeedUser(h, "Sotuvchi", "sot1", Role.Seller, 1);
        SeedUser(h, "Ikkinchi ega", "ega2", Role.Owner, 2);
        // SuperAdmin konsol ro'yxatida ko'rinmaydi — u do'kon xodimi emas.
        SeedUser(h, "Super", "superadmin", Role.SuperAdmin, null);
        // O'chirilgan xodim ham chiqmaydi.
        SeedUser(h, "O'chirilgan", "old", Role.Seller, 1, deleted: true);

        var page = await service.ListAsync(null, null, null, 1, 20);

        Assert.Equal(3, page.Total);
        Assert.DoesNotContain(page.Items, u => u.Username == "superadmin");
        Assert.DoesNotContain(page.Items, u => u.Username == "old");
        Assert.Contains(page.Items, u => u.StoreName == "Ikkinchi");
    }

    [Fact]
    public async Task Filters_narrow_by_role_store_and_free_text()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, "Birinchi");
        SeedMarket(h, 2, "Ikkinchi");
        SeedUser(h, "Sanjar Turaev", "sanjar.t", Role.Owner, 1);
        SeedUser(h, "Jasur Toshev", "jasur.t", Role.Seller, 1);
        SeedUser(h, "Nodira Azimova", "nodira.a", Role.Admin, 2);

        Assert.Equal(1, (await service.ListAsync("Owner", null, null, 1, 20)).Total);
        Assert.Equal(2, (await service.ListAsync(null, 1, null, 1, 20)).Total);
        Assert.Equal("nodira.a", Assert.Single((await service.ListAsync(null, null, "nodira", 1, 20)).Items).Username);
        // Login bo'yicha ham topiladi.
        Assert.Equal(1, (await service.ListAsync(null, null, "jasur.t", 1, 20)).Total);
    }

    [Fact]
    public async Task Resetting_a_password_also_kills_every_live_session()
    {
        var (service, h, epoch) = NewService();
        SeedMarket(h, 1, "Do'kon");
        var user = SeedUser(h, "Sotuvchi", "sot1", Role.Seller, 1);
        var admin = Guid.NewGuid();

        Assert.True(await service.ResetPasswordAsync(user.Id, "YangiParol9!", admin));

        var saved = h.Db.Users.Single(u => u.Id == user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("YangiParol9!", saved.PasswordHash));
        // Access token TTL'i tugagunicha (30 daqiqa) ishlayvermasligi uchun
        // stamp qo'yiladi va kesh yangilanadi.
        Assert.NotNull(saved.TokensInvalidBeforeUtc);
        epoch.Received(1).Publish(user.Id, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task A_weak_password_is_refused_before_anything_changes()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, "Do'kon");
        var user = SeedUser(h, "Sotuvchi", "sot1", Role.Seller, 1);
        var before = h.Db.Users.Single(u => u.Id == user.Id).PasswordHash;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResetPasswordAsync(user.Id, "123", Guid.NewGuid()));

        Assert.Equal(before, h.Db.Users.Single(u => u.Id == user.Id).PasswordHash);
    }

    [Fact]
    public async Task The_platform_admin_cannot_be_touched_from_the_console()
    {
        var (service, h, _) = NewService();
        var super = SeedUser(h, "Super", "superadmin", Role.SuperAdmin, null);

        // Aks holda ikki platforma administratori bir-birini konsol orqali
        // qulflab qo'yishi mumkin edi.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResetPasswordAsync(super.Id, "YangiParol9!", Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetActiveAsync(super.Id, false, Guid.NewGuid()));
    }

    [Fact]
    public async Task Blocking_closes_the_sessions_but_unblocking_does_not_touch_them()
    {
        var (service, h, epoch) = NewService();
        SeedMarket(h, 1, "Do'kon");
        var user = SeedUser(h, "Sotuvchi", "sot1", Role.Seller, 1);

        Assert.True(await service.SetActiveAsync(user.Id, false, Guid.NewGuid()));
        Assert.False(h.Db.Users.Single(u => u.Id == user.Id).IsActive);
        epoch.Received(1).Publish(user.Id, Arg.Any<DateTime>());

        epoch.ClearReceivedCalls();
        Assert.True(await service.SetActiveAsync(user.Id, true, Guid.NewGuid()));
        Assert.True(h.Db.Users.Single(u => u.Id == user.Id).IsActive);
        // Yoqishda sessiya uzish mantiqsiz — foydalanuvchi baribir yangi
        // sessiya ochadi.
        epoch.DidNotReceive().Publish(user.Id, Arg.Any<DateTime>());
    }

    [Fact]
    public async Task Setting_the_state_it_already_has_is_a_no_op()
    {
        var (service, h, _) = NewService();
        SeedMarket(h, 1, "Do'kon");
        var user = SeedUser(h, "Sotuvchi", "sot1", Role.Seller, 1);

        Assert.True(await service.SetActiveAsync(user.Id, true, Guid.NewGuid()));
        Assert.True(h.Db.Users.Single(u => u.Id == user.Id).IsActive);
    }

    [Fact]
    public async Task A_missing_user_reports_not_found_rather_than_throwing()
    {
        var (service, _, _) = NewService();

        Assert.False(await service.ResetPasswordAsync(Guid.NewGuid(), "YangiParol9!", Guid.NewGuid()));
        Assert.False(await service.SetActiveAsync(Guid.NewGuid(), false, Guid.NewGuid()));
    }
}
