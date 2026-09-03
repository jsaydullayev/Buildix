using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// «Qarz amallari uchun bulut bilan aloqa kerak» qoidasi.
///
/// <para><b>Qanday xavfdan himoya qiladi.</b> Ikkita kassa o'z bazasi bilan
/// ishlaganda ular bir-birining qarz yozuvlarini KO'RMAYDI. Ikkalasi ham
/// oflayn holda bitta mijozga qarz bera oladi (chegara ikki marta
/// sarflanadi), bitta qarzni ikki marta undira oladi yoki bitta avansni ikki
/// marta sarflay oladi. Hech biri xato bermaydi — raqamlar keyin,
/// birlashganda to'g'ri kelmay qoladi.</para>
///
/// <para><b>Eng muhim sinov — oxirgisi:</b> qoida NAQD savdoga tegmasligi
/// kerak. Aks holda internet uzilgan do'kon umuman sotolmay qolardi, holbuki
/// naqd pul o'sha yerda va o'sha zahoti olinadi.</para>
/// </summary>
public class DebtCloudGateTests
{
    private const int Market = 1;

    private static void RequireCloud(TestHarness h, bool on) =>
        h.Settings.GetOrCreateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MarketSettings
            {
                SalesOnlyWhenShiftOpen = false,
                DebtOnlyForRegulars = false,
                BlockSaleBelowCost = false,
                CashWithdrawalNeedsApproval = false,
                DefaultDebtLimit = 0m,
                DebtRequiresCloud = on,
            });

    private static (Sale Sale, Customer Customer) NewDebtCandidate(TestHarness h)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement", MarketId = Market,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = 100, MinThreshold = 1, Unit = UnitType.Piece,
        };
        h.Db.Products.Add(product);

        var customer = new Customer
        {
            Id = Guid.NewGuid(), MarketId = Market, Phone = "+998901112233", FullName = "Xoshim",
        };
        h.Db.Customers.Add(customer);

        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = Guid.NewGuid(),
            CustomerId = customer.Id, SaleNumber = 1, Status = SaleStatus.Draft,
            TotalAmount = 200_000, PaidAmount = 0,
        };
        h.Db.Sales.Add(sale);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 4, SalePrice = 50_000, CostPrice = 30_000,
        });
        return (sale, customer);
    }

    /// <summary>
    /// Qoida O'CHIQ bo'lsa — oflayn ham qarz yoziladi.
    /// </summary>
    /// <remarks>
    /// Bugungi holat. Bitta bazali do'konda (ikkita LAN kassasi ham shunga
    /// kiradi) qarz yozuvi bitta joyda turadi va u har doim o'ziga o'zi mos —
    /// aloqa talab qilishning ma'nosi yo'q.
    /// </remarks>
    [Fact]
    public async Task Qoida_ochiq_bolsa_oflayn_ham_qarz_yoziladi()
    {
        using var h = new TestHarness(Market);
        RequireCloud(h, on: false);
        var (sale, _) = NewDebtCandidate(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Freshness.IsPaired = false;   // umuman bog'lanmagan
        h.Freshness.IsFresh = false;

        var result = await h.NewSaleService()
            .MarkSaleAsDebtAsync(sale.Id, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>Qoida yoqilgan va ma'lumot yangi — qarz o'tadi.</summary>
    [Fact]
    public async Task Aloqa_bor_bolsa_qarz_otadi()
    {
        using var h = new TestHarness(Market);
        RequireCloud(h, on: true);
        var (sale, _) = NewDebtCandidate(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.NewSaleService()
            .MarkSaleAsDebtAsync(sale.Id, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// Ma'lumot eskirgan yoki aloqa yo'q — qarz RAD etiladi.
    /// </summary>
    [Theory]
    [InlineData(false, true, null)]              // bog'lanmagan
    [InlineData(true, false, null)]              // eskirgan
    [InlineData(true, true, "tashqi kalit xatosi")]  // aloqa bor, lekin sinxronizatsiya buzilgan
    public async Task Aloqa_yoq_bolsa_qarz_rad_etiladi(bool paired, bool fresh, string? error)
    {
        using var h = new TestHarness(Market);
        RequireCloud(h, on: true);
        var (sale, _) = NewDebtCandidate(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Freshness.IsPaired = paired;
        h.Freshness.IsFresh = fresh;
        h.Freshness.Error = error;

        var result = await h.NewSaleService()
            .MarkSaleAsDebtAsync(sale.Id, Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("DEBT_NEEDS_CLOUD", result.Code);

        // Chek qarzga o'tmagan va qarz yozuvi ham yaratilmagan.
        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == sale.Id);
        Assert.Equal(SaleStatus.Draft, stored.Status);
        Assert.Empty(await h.Db.Debts.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// QISMAN TO'LOV yo'li ham to'siladi.
    /// </summary>
    /// <remarks>
    /// Qisman to'lov ham qarz yaratadi. Faqat «Qarzga» tugmasi tekshirilsa,
    /// qoida bitta tugmani chetlab o'tish bilan yo'qolardi — kassir 1 so'm
    /// to'lov qabul qilib o'sha qarzni yozaverardi.
    /// </remarks>
    [Fact]
    public async Task Qisman_tolov_yoli_ham_tosiladi()
    {
        using var h = new TestHarness(Market);
        RequireCloud(h, on: true);
        var (sale, _) = NewDebtCandidate(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Freshness.IsFresh = false;

        var result = await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 50_000m, null));

        Assert.True(result.IsFailure);
        Assert.Equal("DEBT_NEEDS_CLOUD", result.Code);
    }

    /// <summary>
    /// NAQD savdo tegilmaydi — qoida yoqilgan va aloqa yo'q bo'lsa ham.
    /// </summary>
    /// <remarks>
    /// <para>Eng muhim chegara. Naqd pul o'sha yerda va o'sha zahoti
    /// olinadi — uni ikki marta olib bo'lmaydi, ya'ni oflayn ham xavfsiz.
    /// Qoida to'liq to'langan chekka tegsa, internet uzilgan do'kon umuman
    /// sotolmay qolardi.</para>
    /// </remarks>
    [Fact]
    public async Task Naqd_savdo_tegilmaydi()
    {
        using var h = new TestHarness(Market);
        RequireCloud(h, on: true);
        var (sale, _) = NewDebtCandidate(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Freshness.IsPaired = false;
        h.Freshness.IsFresh = false;

        // To'liq summa — qarz qolmaydi.
        var result = await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 200_000m, null));

        Assert.True(result.IsSuccess, result.Error);
        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == sale.Id);
        Assert.Equal(200_000m, stored.PaidAmount);
    }
}
