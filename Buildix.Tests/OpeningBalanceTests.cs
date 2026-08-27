using Buildix.Application.DTOs;
using Buildix.Application.Services;
using Buildix.Application.Services.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buildix.Tests;

/// <summary>
/// Mijozning tizimdan OLDINGI qarzi.
///
/// <para>Qarz har doim savdoga bog'lanadi, eski qarzning esa savdosi yo'q —
/// shu sababli tovarsiz texnik qator yaratiladi. Bu yerdagi tekshiruvlar
/// bitta narsani qo'riqlaydi: o'sha qator TUSHUM emas. Ilgari u oddiy savdo
/// bo'lib hisoblanardi va mijoz kiritilgan kuni tushum qarz summasiga
/// ko'tarilib ketardi — hisobotda esa uning ortida hech qanday tovar
/// ko'rinmasdi.</para>
/// </summary>
public class OpeningBalanceTests
{
    private static SalesReportService NewReportService(TestHarness h) =>
        new(h.UnitOfWork, h.Market, h.Db, h.Clock, NullLogger<SalesReportService>.Instance);

    /// <summary>Haqiqiy savdo — tovar qatori bilan.</summary>
    private static async Task<Sale> NewRealSaleAsync(TestHarness h, decimal amount)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "sement", MarketId = 1,
            Quantity = 100, MinThreshold = 10,
            SalePrice = amount, CostPrice = amount / 2, MinSalePrice = amount,
        };
        h.Db.Products.Add(product);

        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = 1, SellerId = Guid.NewGuid(),
            Status = SaleStatus.Paid, TotalAmount = amount, PaidAmount = amount,
            CreatedAt = DateTime.UtcNow,
        };
        h.Db.Sales.Add(sale);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 1, SalePrice = amount, CostPrice = amount / 2,
        });
        await h.Db.SaveChangesAsync();
        return sale;
    }

    /// <summary>Eski qarzning texnik qatori — tovarsiz.</summary>
    private static async Task<Sale> NewOpeningBalanceAsync(TestHarness h, decimal amount)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = 1, SellerId = Guid.NewGuid(),
            Status = SaleStatus.Debt, TotalAmount = amount, PaidAmount = 0,
            IsOpeningBalance = true, CreatedAt = DateTime.UtcNow,
        };
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        return sale;
    }

    private static Task<PeriodReportDto> ReportAsync(TestHarness h) =>
        NewReportService(h).GetPeriodReportAsync(
            new PeriodReportRequest(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow),
            canViewProfit: true);

    /// <summary>
    /// Foydalanuvchi aytgan holat: bir kunda 150 000 lik savdo bo'ldi va
    /// o'sha kuni eski qarz kiritildi. Tushum 150 000 bo'lib qolishi kerak.
    /// </summary>
    [Fact]
    public async Task Eski_qarz_tushumga_qoshilmaydi()
    {
        using var h = new TestHarness();
        await NewRealSaleAsync(h, 150_000);
        await NewOpeningBalanceAsync(h, 1_000_000);

        var report = await ReportAsync(h);

        Assert.Equal(150_000m, report.TotalSales);
    }

    [Fact]
    public async Task Eski_qarz_chek_soniga_qoshilmaydi()
    {
        using var h = new TestHarness();
        await NewRealSaleAsync(h, 150_000);
        await NewOpeningBalanceAsync(h, 1_000_000);

        var report = await ReportAsync(h);

        // Bitta chek — o'rtacha chek ham shundan hisoblanadi.
        Assert.Equal(1, report.TotalTransactions);
        Assert.Equal(150_000m, report.AverageSale);
    }

    /// <summary>
    /// Marja eng sezilarli buzilgan joy edi: tovarsiz million so'm tushumga
    /// qo'shilib, foizni deyarli nolga tushirardi.
    /// </summary>
    [Fact]
    public async Task Eski_qarz_marjani_buzmaydi()
    {
        using var h = new TestHarness();
        await NewRealSaleAsync(h, 150_000);   // tannarxi 75 000 → foyda 75 000
        await NewOpeningBalanceAsync(h, 1_000_000);

        var report = await ReportAsync(h);

        Assert.Equal(75_000m, report.Profit);
        // Marja 50% — million so'm qo'shilganda u 6% ga tushib ketardi.
        Assert.Equal(0.5m, report.Profit!.Value / report.TotalSales);
    }

    /// <summary>
    /// Belgi FAQAT eski qarzga tegishli. Oddiy qarzli savdo (tovar berilgan,
    /// puli keyin) tushumda qolishi shart — aks holda kunlik savdo
    /// kamayib ketardi.
    /// </summary>
    [Fact]
    public async Task Oddiy_qarzli_savdo_tushumda_qoladi()
    {
        using var h = new TestHarness();
        var sale = await NewRealSaleAsync(h, 150_000);
        sale.Status = SaleStatus.Debt;
        sale.PaidAmount = 0;
        await h.Db.SaveChangesAsync();

        var report = await ReportAsync(h);

        Assert.Equal(150_000m, report.TotalSales);
        Assert.Equal(1, report.TotalTransactions);
    }

    /// <summary>
    /// Mijoz kiritilganda yaratiladigan qator BELGILANGAN bo'lishi kerak.
    /// Belgisiz qolsa, tuzatish o'z-o'zidan bekor bo'lardi.
    /// </summary>
    [Fact]
    public async Task Mijoz_kiritilganda_yozuv_belgilanadi()
    {
        using var h = new TestHarness();
        var userId = Guid.NewGuid();
        h.Db.Users.Add(new User
        {
            Id = userId, MarketId = 1, Username = "ega", FullName = "Ega",
            PasswordHash = "x", Role = Role.Owner, IsActive = true,
        });
        await h.Db.SaveChangesAsync();
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor
        {
            HttpContext = NewContextWithUser(userId),
        };

        var service = new CustomerService(h.UnitOfWork, h.Db, h.Market, accessor, h.Clock);
        var created = await service.CreateCustomerAsync(new CreateCustomerDto(
            Phone: "+998901234567", FullName: "Anvar", Comment: null, InitialDebt: 1_000_000));

        Assert.True(created.IsSuccess, created.Error);

        var sale = await h.Db.Sales.IgnoreQueryFilters().SingleAsync();
        Assert.True(sale.IsOpeningBalance, "eski qarz qatori belgilanmagan");
        Assert.Equal(1_000_000m, sale.TotalAmount);

        // Qarzning o'zi joyida — u yo'qolmasligi kerak.
        var debt = await h.Db.Debts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1_000_000m, debt.RemainingDebt);

        // Va u tushumga kirmaydi.
        Assert.Equal(0m, (await ReportAsync(h)).TotalSales);
    }

    /// <summary>
    /// Kassa oynasining «bugungi savdo» paneli ham shu qoidaga bo'ysunadi:
    /// mijoz kiritilgan kuni u yerda million so'mlik «chek» chiqib qolmasin.
    /// </summary>
    [Fact]
    public async Task Eski_qarz_kassa_kunlik_panelida_korinmaydi()
    {
        using var h = new TestHarness();
        await NewRealSaleAsync(h, 150_000);
        await NewOpeningBalanceAsync(h, 1_000_000);

        var summary = await h.NewCashRegisterService().GetTodaySalesSummaryAsync();

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.TotalSales);
        Assert.Equal(150_000m, summary.TotalAmount);
        // Qarz paneli ham: eski qarz bu yerda emas, «Qarzlar» bo'limida turadi.
        Assert.Equal(0m, summary.DebtAmount);
    }

    private static Microsoft.AspNetCore.Http.HttpContext NewContextWithUser(Guid userId)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString())],
                "test"));
        return ctx;
    }
}
