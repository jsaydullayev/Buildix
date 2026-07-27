using Buildix.Application.DTOs;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// S3 — «Оплата получена» matematikasi (V4: grace'ga qarab langar).
///
/// <para>Bu amal QAYTARIB BO'LMAYDI: to'lov qatori o'chirilmaydi va do'konning
/// obuna muddati o'zgaradi. Shuning uchun har bir chegara holati shu yerda
/// qotirilgan.</para>
/// </summary>
public class SubscriptionBillingTests
{
    private static readonly DateTime Now = new(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc);
    // Otsrochka endi platforma sozlamasidan keladi — testlar uni ATAYLAB
    // aniq qiymat bilan beradi (default 5).
    private const int Grace = 5;

    private sealed class FixedClock : Buildix.Application.Interfaces.ITashkentClock
    {
        public DateTime UtcNow => Now;
        public DateTime NowLocal => Now.AddHours(5);
        public DateTime TodayLocal => new(2026, 8, 3);
        public (DateTime UtcStart, DateTime UtcEnd) LocalDayToUtcRange(DateTime d)
            => (DateTime.SpecifyKind(d.Date.AddHours(-5), DateTimeKind.Utc),
                DateTime.SpecifyKind(d.Date.AddHours(19), DateTimeKind.Utc));
        public DateTime ToLocal(DateTime utc) => utc.AddHours(5);
    }

    // ── Sof matematik qoida ────────────────────────────────────────────────

    [Fact]
    public void Early_payment_keeps_the_billing_day()
    {
        // 28-iyulda tugaydi, 20-iyulda to'laydi → 28-avgust (qolgan kunlar
        // yo'qolmaydi va hisob kuni surilmaydi).
        var expiry = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
        var (result, anchored) = SuperAdminBillingService.Extend(
            expiry, months: 1, nowUtc: new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc), graceDays: Grace);

        Assert.Equal(new DateTime(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc), result);
        Assert.True(anchored);
    }

    [Fact]
    public void Payment_inside_the_grace_window_still_anchors_on_the_old_date()
    {
        // Muddat 1-avgustda tugadi, bugun 3-avgust (otsrochka ichida) — do'kon
        // shu ikki kun ISHLADI, demak o'sha kunlar uchun to'laydi.
        var expiry = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var (result, anchored) = SuperAdminBillingService.Extend(expiry, 1, Now, Grace);

        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), result);
        Assert.True(anchored);
    }

    [Fact]
    public void Payment_after_the_grace_window_anchors_on_today()
    {
        // Xizmat uzilgan edi — o'chiq turgan davr uchun pul olinmaydi.
        var expiry = Now.AddDays(-(Grace + 1));
        var (result, anchored) = SuperAdminBillingService.Extend(expiry, 1, Now, Grace);

        Assert.Equal(Now.AddMonths(1), result);
        Assert.False(anchored);
    }

    [Fact]
    public void The_last_grace_day_still_counts_as_served()
    {
        // Chegaraning aynan o'zi: grace oxirgi kuni — hali uzilmagan.
        var expiry = Now.AddDays(-Grace);
        var (_, anchored) = SuperAdminBillingService.Extend(expiry, 1, Now, Grace);
        Assert.True(anchored);

        // Bir soniya keyin — uzilgan.
        var (_, anchoredAfter) = SuperAdminBillingService.Extend(expiry.AddSeconds(-1), 1, Now, Grace);
        Assert.False(anchoredAfter);
    }

    [Fact]
    public void A_store_with_no_expiry_starts_counting_from_today()
    {
        var (result, anchored) = SuperAdminBillingService.Extend(null, 3, Now, Grace);

        Assert.Equal(Now.AddMonths(3), result);
        Assert.False(anchored);
    }

    [Fact]
    public void Multi_month_payments_add_calendar_months_not_thirty_day_blocks()
    {
        // 31-yanvar + 1 oy = 28-fevral (kalendar), 2-mart emas.
        var expiry = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var (result, _) = SuperAdminBillingService.Extend(
            expiry, 1, new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc), Grace);

        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), result);
    }

    // ── Yozuv yo'li ────────────────────────────────────────────────────────

    private static (SuperAdminBillingService Service, TestHarness H) NewService()
    {
        var h = new TestHarness(marketId: null);
        // InMemory HasData seed'ni qo'llamaydi — tariflarni qo'lda kiritamiz.
        h.Db.PlatformPlans.AddRange(
            new PlatformPlan { Code = PlanCode.Start, PriceUzs = 600_000m, MaxUsers = 3, MaxPoints = 1 },
            new PlatformPlan { Code = PlanCode.Standard, PriceUzs = 1_200_000m, MaxUsers = 8, MaxPoints = 1 },
            new PlatformPlan { Code = PlanCode.Pro, PriceUzs = 2_400_000m, MaxUsers = 0, MaxPoints = 3 });
        h.Db.SaveChanges();
        return (new SuperAdminBillingService(h.Db, new FixedClock(), h.Audit, FixedPlatformSettings.Default), h);
    }

    private static void SeedMarket(TestHarness h, int id, PlanCode plan, DateTime? expiresAt)
    {
        h.Db.Markets.Add(new Market
        {
            Id = id, Name = $"Do'kon {id}", Plan = plan, ExpiresAt = expiresAt,
            IsActive = true, OwnerId = Guid.NewGuid(), CreatedAt = Now.AddMonths(-2),
        });
        h.Db.SaveChanges();
    }

    [Fact]
    public async Task Recording_a_payment_writes_the_row_and_extends_the_subscription_together()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, PlanCode.Standard, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var admin = Guid.NewGuid();

        var result = await service.RecordAsync(
            1, new SaRecordPaymentDto(Months: 1, Channel: "Click"), admin);

        Assert.NotNull(result);
        Assert.Equal(1_200_000m, result!.AmountUzs);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), result.NewExpiresAt);

        var payment = Assert.Single(h.Db.SubscriptionPayments);
        Assert.Equal(PlanCode.Standard, payment.Plan);
        Assert.Equal(admin, payment.AcceptedByUserId);
        // Do'konning muddati to'lov qatori bilan BIR VAQTDA yangilanadi.
        Assert.Equal(result.NewExpiresAt, h.Db.Markets.Single(m => m.Id == 1).ExpiresAt);
        Assert.Equal(result.NewExpiresAt, payment.PeriodEndUtc);
    }

    [Fact]
    public async Task The_amount_is_the_plan_price_times_the_months()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, PlanCode.Start, Now.AddMonths(1));

        var result = await service.RecordAsync(
            1, new SaRecordPaymentDto(Months: 6, Channel: "Cash"), Guid.NewGuid());

        Assert.Equal(3_600_000m, result!.AmountUzs);
        Assert.Equal(6, Assert.Single(h.Db.SubscriptionPayments).Months);
    }

    [Fact]
    public async Task Switching_the_plan_bills_at_the_new_price_in_one_action()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, PlanCode.Start, Now.AddMonths(1));

        var result = await service.RecordAsync(
            1, new SaRecordPaymentDto(Months: 1, Channel: "Payme", Plan: "Pro"),
            Guid.NewGuid());

        Assert.Equal(2_400_000m, result!.AmountUzs);
        Assert.Equal(PlanCode.Pro, h.Db.Markets.Single(m => m.Id == 1).Plan);
    }

    [Fact]
    public async Task A_frozen_amount_survives_a_later_price_change()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, PlanCode.Standard, Now.AddMonths(1));
        await service.RecordAsync(1, new SaRecordPaymentDto(1, "Cash"), Guid.NewGuid());

        // Operator narxni ko'tardi — o'tgan to'lov tarixi o'zgarmasligi kerak.
        h.Db.PlatformPlans.Single(p => p.Code == PlanCode.Standard).PriceUzs = 1_500_000m;
        h.Db.SaveChanges();

        Assert.Equal(1_200_000m, Assert.Single(h.Db.SubscriptionPayments).AmountUzs);
    }

    [Fact]
    public async Task The_preview_returns_exactly_what_recording_would_write()
    {
        var (service, h) = NewService();
        var expiry = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedMarket(h, 1, PlanCode.Standard, expiry);

        var preview = await service.PreviewAsync(1, months: 3);
        var result = await service.RecordAsync(1, new SaRecordPaymentDto(3, "Cash"), Guid.NewGuid());

        // Modal ko'rsatgan sana bilan yozilgani bir xil bo'lishi SHART —
        // operator ko'rgan narsasiga ishonadi.
        Assert.Equal(preview!.NewExpiresAt, result!.NewExpiresAt);
        Assert.Equal(preview.AmountUzs, result.AmountUzs);
        Assert.True(preview.AnchoredOnExpiry);
    }

    [Fact]
    public async Task A_deleted_store_cannot_be_charged()
    {
        var (service, h) = NewService();
        h.Db.Markets.Add(new Market
        {
            Id = 9, Name = "O'chirilgan", Plan = PlanCode.Start, IsActive = false,
            OwnerId = Guid.NewGuid(), ExpiresAt = Now,
        });
        h.Db.SaveChanges();

        Assert.Null(await service.RecordAsync(9, new SaRecordPaymentDto(1, "Cash"), Guid.NewGuid()));
        Assert.Empty(h.Db.SubscriptionPayments);
    }

    [Fact]
    public void The_grace_setting_actually_drives_the_payment_anchor()
    {
        // S5 dan keyingi ko'rikda topilgan nomuvofiqlik: otsrochka konsolda
        // sozlanadi, lekin to'lov matematikasi qattiq «5» ni ishlatardi.
        // Endi ikkalasi bitta qiymatdan oziqlanadi.
        var expiry = Now.AddDays(-8);

        // Otsrochka 5 kun bo'lsa — xizmat uzilgan, langar bugun.
        var (shortGrace, anchoredShort) = SuperAdminBillingService.Extend(expiry, 1, Now, graceDays: 5);
        Assert.False(anchoredShort);
        Assert.Equal(Now.AddMonths(1), shortGrace);

        // Operator otsrochkani 10 kunga ko'tardi — do'kon hali ham xizmatda
        // edi, demak langar eski muddat va hisob kuni surilmaydi.
        var (longGrace, anchoredLong) = SuperAdminBillingService.Extend(expiry, 1, Now, graceDays: 10);
        Assert.True(anchoredLong);
        Assert.Equal(expiry.AddMonths(1), longGrace);
    }

    [Fact]
    public async Task The_billing_list_separates_soon_from_overdue()
    {
        var (service, h) = NewService();
        SeedMarket(h, 1, PlanCode.Start, Now.AddDays(30));   // Active
        SeedMarket(h, 2, PlanCode.Start, Now.AddDays(3));    // Soon (<7 kun)
        SeedMarket(h, 3, PlanCode.Start, Now.AddDays(-2));   // Overdue

        var rows = await service.ListAsync();

        Assert.Equal("Active", rows.Single(r => r.MarketId == 1).Status);
        Assert.Equal("Soon", rows.Single(r => r.MarketId == 2).Status);
        Assert.Equal("Overdue", rows.Single(r => r.MarketId == 3).Status);
        // Eng shoshilinchi yuqorida — muddat bo'yicha o'sish tartibida.
        Assert.Equal(3, rows[0].MarketId);
    }
}
