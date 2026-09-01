using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Yashikdan pul chiqsa, u «Касса» JURNALIDA ham ko'rinishi shart.
///
/// <para><b>Nima buzilgan edi.</b> Ikkita yo'l kassa qoldig'ini kamaytirar,
/// lekin jurnalga hech narsa yozmasdi: chekni bekor qilish (naqd qaytariladi)
/// va tasdiqlangan naqd yechish. Egasi ekranda qoldiq tushganini ko'rar,
/// «Расход» ro'yxatida esa unga mos qator YO'Q edi — pul sababsiz kamaygandek
/// ko'rinardi va uni qayerga ketganini jurnaldan topib bo'lmasdi.</para>
///
/// <para>Bu ko'p kassali rejimga o'tishning sharti hamdir: ikkita mustaqil
/// kassa bir do'kon nomidan ishlaganda balans ustuni birlashtirilmaydi
/// (oxirgi yozgan g'olib chiqadi), jurnal esa qo'shiladigan — qatorlar
/// shunchaki yig'iladi. Buning uchun jurnal TO'LIQ bo'lishi kerak.</para>
/// </summary>
public class CashLedgerCompletenessTests
{
    private const int Market = 1;

    private static Product AddProduct(TestHarness h)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement", MarketId = Market,
            CostPrice = 30_000, SalePrice = 50_000, MinSalePrice = 40_000,
            Quantity = 100, MinThreshold = 1, Unit = UnitType.Piece,
        };
        h.Db.Products.Add(product);
        return product;
    }

    /// <summary>Yashikdagi pul (jurnaldagi chiqim/kirimlar yig'indisi).</summary>
    private static async Task<decimal> LedgerSumAsync(TestHarness h) =>
        await h.Db.CashMovements.IgnoreQueryFilters().SumAsync(m => m.Amount);

    private static async Task<decimal> BalanceAsync(TestHarness h) =>
        await h.Db.CashRegisters.IgnoreQueryFilters()
            .Select(c => c.CurrentBalance).FirstAsync();

    /// <summary>
    /// Naqd to'langan chek bekor qilinsa — pul yashikdan chiqadi va bu
    /// jurnalda ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Bekor_qilingan_chek_jurnalga_tushadi()
    {
        using var h = new TestHarness(Market);
        var product = AddProduct(h);
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = Guid.NewGuid(),
            SaleNumber = 7, Status = SaleStatus.Draft,
        };
        h.Db.Sales.Add(sale);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewSaleItemService().AddSaleItemAsync(
            sale.Id, new AddSaleItemDto(false, product.Id, null, null, 3m, 50_000m, 40_000m, null));
        await h.NewSalePaymentService()
            .AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 150_000m, null));

        var beforeBalance = await BalanceAsync(h);
        var beforeLedger = await LedgerSumAsync(h);
        Assert.Equal(150_000m, beforeBalance);

        h.Db.ChangeTracker.Clear();
        var cancelled = await h.NewSaleReversalService()
            .CancelSaleAsync(sale.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.Error);

        // Balans ham, jurnal ham AYNAN bir xil miqdorda harakatlandi.
        Assert.Equal(0m, await BalanceAsync(h));
        Assert.Equal(beforeLedger - 150_000m, await LedgerSumAsync(h));

        var refundRow = await h.Db.CashMovements.IgnoreQueryFilters()
            .OrderByDescending(m => m.CreatedAt).FirstAsync(m => m.Amount < 0);
        Assert.Equal(-150_000m, refundRow.Amount);
        Assert.Equal(7, refundRow.RefNumber);
    }

    /// <summary>
    /// Tasdiq talab qiladigan yechish: so'rov paytida pul yashikda qoladi,
    /// tasdiqlanganda chiqadi — va o'shanda jurnalga tushadi.
    /// </summary>
    [Fact]
    public async Task Tasdiqlangan_yechish_jurnalga_tushadi()
    {
        using var h = new TestHarness(Market);
        h.Db.CashRegisters.Add(new CashRegister
        {
            Id = Guid.NewGuid(), MarketId = Market, CurrentBalance = 500_000m,
        });
        var owner = new User
        {
            Id = Guid.NewGuid(), MarketId = Market, Username = "ega", FullName = "Ega",
            PasswordHash = "x", Role = Role.Owner, IsActive = true,
        };
        h.Db.Users.Add(owner);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var cash = h.NewCashRegisterService();
        var requested = await cash.RequestWithdrawalAsync(
            new WithdrawCashRequest { Amount = 120_000m, Comment = "Yetkazib beruvchiga" },
            owner.Id);
        Assert.True(requested.IsSuccess, requested.Error);

        // So'rov paytida pul HALI yashikda va jurnalda ham iz yo'q.
        Assert.Equal(500_000m, await BalanceAsync(h));
        Assert.Equal(0m, await LedgerSumAsync(h));

        var pending = await h.Db.CashWithdrawals.IgnoreQueryFilters().FirstAsync();
        var approved = await cash.ApproveWithdrawalAsync(pending.Id, owner.Id);
        Assert.True(approved.IsSuccess, approved.Error);

        Assert.Equal(380_000m, await BalanceAsync(h));
        Assert.Equal(-120_000m, await LedgerSumAsync(h));
    }
}
