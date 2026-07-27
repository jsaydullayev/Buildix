using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Buildix.Application.Interfaces;

namespace Buildix.Tests;

/// <summary>
/// S1 — ariza oqimi: Новая → Принята → «Создать магазин» → Подключена, va
/// Отклонена ↔ Вернуть. «Принять» qadami do'kon YARATMAYDI: uning yagona vazifasi
/// arizani «yangi»lar ro'yxatidan olib, operator ikkinchi marta qo'ng'iroq
/// qilmasligi.
/// </summary>
public class RegistrationRequestFlowTests
{
    private static (RegistrationRequestService Service, TestHarness H) NewService()
    {
        // MarketId = null — konsol so'rovlari hech qaysi tenantga tegishli emas.
        var h = new TestHarness(marketId: null);
        var service = new RegistrationRequestService(
            h.Db,
            NullLogger<RegistrationRequestService>.Instance,
            h.Audit,
            Substitute.For<IUserTokenEpochStore>());
        return (service, h);
    }

    private static RegistrationRequest Seed(TestHarness h, RegistrationRequestStatus status)
    {
        var r = new RegistrationRequest
        {
            Id = Guid.NewGuid(),
            FullName = "Aziz Karimov",
            Phone = "+998901234567",
            Note = "stroymateriallar, Toshkent",
            Status = status,
            CreatedAt = DateTime.UtcNow,
        };
        h.Db.RegistrationRequests.Add(r);
        h.Db.SaveChanges();
        return r;
    }

    [Fact]
    public async Task Accept_moves_a_new_request_out_of_the_new_list_without_creating_anything()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Pending);
        var admin = Guid.NewGuid();

        Assert.True(await service.SetStatusAsync(req.Id, RegistrationRequestStatus.Accepted, admin));

        var saved = h.Db.RegistrationRequests.Single(r => r.Id == req.Id);
        Assert.Equal(RegistrationRequestStatus.Accepted, saved.Status);
        Assert.Equal(admin, saved.ProcessedByUserId);
        Assert.NotNull(saved.ProcessedAt);
        // Hech qanday do'kon/owner yaratilmadi — bu «Создать магазин» ning ishi.
        Assert.Null(saved.CreatedMarketId);
        Assert.Null(saved.CreatedUserId);
        Assert.Empty(h.Db.Markets);
    }

    [Fact]
    public async Task Reopen_clears_the_review_trail()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Rejected);
        req.RejectReason = "telefon ko'tarmadi";
        req.ProcessedAt = DateTime.UtcNow;
        req.ProcessedByUserId = Guid.NewGuid();
        h.Db.SaveChanges();

        Assert.True(await service.SetStatusAsync(req.Id, RegistrationRequestStatus.Pending, Guid.NewGuid()));

        var saved = h.Db.RegistrationRequests.Single(r => r.Id == req.Id);
        Assert.Equal(RegistrationRequestStatus.Pending, saved.Status);
        // Aks holda ro'yxatda «yangi, lekin rad etish sababi bor» degan
        // qarama-qarshi qator turib qolardi.
        Assert.Null(saved.RejectReason);
        Assert.Null(saved.ProcessedAt);
        Assert.Null(saved.ProcessedByUserId);
    }

    [Fact]
    public async Task A_connected_request_can_never_be_reopened()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Approved);
        req.CreatedMarketId = 7;
        h.Db.SaveChanges();

        // Do'kon allaqachon bor — arizani «yangi» holatiga qaytarish market va
        // owner bilan aloqani uzib qo'yardi.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetStatusAsync(req.Id, RegistrationRequestStatus.Pending, Guid.NewGuid()));

        Assert.Equal(RegistrationRequestStatus.Approved,
            h.Db.RegistrationRequests.Single(r => r.Id == req.Id).Status);
    }

    [Fact]
    public async Task Setting_the_status_it_already_has_is_a_no_op()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Accepted);

        // Ikki marta bosilgan tugma xato bermasin.
        Assert.True(await service.SetStatusAsync(req.Id, RegistrationRequestStatus.Accepted, Guid.NewGuid()));
        Assert.Equal(RegistrationRequestStatus.Accepted,
            h.Db.RegistrationRequests.Single(r => r.Id == req.Id).Status);
    }

    [Fact]
    public async Task Approved_is_not_reachable_by_hand()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Pending);

        // «Подключена» faqat ApproveAsync orqali — u do'kon va ownerni ham
        // yaratadi. Aks holda ariza ulangan ko'rinib, do'kon bo'lmasdi.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetStatusAsync(req.Id, RegistrationRequestStatus.Approved, Guid.NewGuid()));
    }

    [Fact]
    public async Task An_accepted_request_can_still_be_rejected()
    {
        var (service, h) = NewService();
        var req = Seed(h, RegistrationRequestStatus.Accepted);

        // Qo'ng'iroqdan keyin mijoz fikridan qaytishi odatiy hol.
        Assert.True(await service.RejectAsync(req.Id, "fikridan qaytdi", Guid.NewGuid()));

        var saved = h.Db.RegistrationRequests.Single(r => r.Id == req.Id);
        Assert.Equal(RegistrationRequestStatus.Rejected, saved.Status);
        Assert.Equal("fikridan qaytdi", saved.RejectReason);
    }

    [Fact]
    public async Task The_list_marks_only_a_request_with_a_real_market_as_connected()
    {
        var (service, h) = NewService();
        var connected = Seed(h, RegistrationRequestStatus.Approved);
        connected.CreatedMarketId = 3;
        var accepted = Seed(h, RegistrationRequestStatus.Accepted);
        h.Db.SaveChanges();

        var rows = (await service.ListAsync(status: null)).ToList();

        Assert.True(rows.Single(r => r.Id == connected.Id).IsConnected);
        Assert.False(rows.Single(r => r.Id == accepted.Id).IsConnected);
        // Izoh ro'yxatga chiqadi — operator qo'ng'iroqdan oldin ko'radi.
        Assert.Equal("stroymateriallar, Toshkent", rows.First().Note);
    }
}
