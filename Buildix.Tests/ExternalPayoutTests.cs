using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Qo'shni do'kondan olingan tovar uchun kassadan chiqqan pul.
///
/// <para>Mijoz so'ragan narsa bizda bo'lmasa, u qo'shni do'kondan olinadi va
/// puli kassadan beriladi. Ilgari bu chiqim hech qayerda qayd etilmasdi:
/// mijozdan olingan pul to'liq kassada ko'rinar, qo'shniga berilgani esa yo'q —
/// natijada smena yakunida naqd doim kamayib chiqardi.</para>
///
/// <para>Bu testlar uchta mustaqil hisobning ham to'g'ri qolishini qo'riqlaydi:
/// <c>CashRegister.CurrentBalance</c> (haqiqiy balans), <c>CashMovement</c>
/// (Касса ro'yxati) va <c>Shift.ExpectedCash</c> (sverka).</para>
/// </summary>
public class ExternalPayoutTests
{
    private const int Market = 1;

    /// <summary>
    /// Draft sotuv: bitta oddiy qator + bitta tashqi qator. Tashqi qator
    /// <paramref name="externalCost"/> ga olinib <paramref name="externalPrice"/>
    /// ga sotiladi.
    /// </summary>
    private static async Task<Sale> SeedSaleAsync(
        TestHarness h, Guid sellerId,
        decimal ownPrice = 100_000m,
        decimal externalCost = 60_000m, decimal externalPrice = 80_000m,
        decimal externalQty = 1m, Guid? customerId = null, Guid? shiftId = null)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "O'zimizniki", MarketId = Market,
            CostPrice = ownPrice / 2, SalePrice = ownPrice, MinSalePrice = ownPrice / 2,
            Quantity = 1000, MinThreshold = 1, Unit = UnitType.Piece,
        };
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = sellerId, CustomerId = customerId,
            Status = SaleStatus.Draft, MarketId = Market, ShiftId = shiftId,
            SaleNumber = Random.Shared.Next(1, 100_000),
            // Haqiqiy oqimda SaleItemService buni qatorlar bilan bir xilda ushlab
            // turadi; «qarzga» yo'li esa jamini qayta hisoblamasdan o'qiydi.
            TotalAmount = ownPrice + externalPrice * externalQty,
        };
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 1, SalePrice = ownPrice, CostPrice = product.CostPrice, IsExternal = false,
        });
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = null, IsExternal = true,
            ExternalProductName = "Qo'shnidan", ExternalCostPrice = externalCost,
            Quantity = externalQty, SalePrice = externalPrice,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return sale;
    }

    private static Task<decimal> BalanceAsync(TestHarness h) =>
        h.Db.CashRegisters.IgnoreQueryFilters()
            .Where(cr => cr.MarketId == Market)
            .Select(cr => cr.CurrentBalance)
            .FirstAsync();

    private static Task<List<CashMovement>> PayoutMovementsAsync(TestHarness h) =>
        h.Db.CashMovements.IgnoreQueryFilters()
            .Where(m => m.Type == CashMovementType.ExternalPurchase)
            .ToListAsync();

    [Fact]
    public async Task Payout_leaves_till_with_only_our_own_margin()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid());  // 100k o'ziniki + 80k tashqi (60k ga olingan)

        // Mijoz 180 000 to'laydi, undan 60 000 qo'shniga beriladi.
        var result = await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 180_000));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(120_000m, await BalanceAsync(h));

        var movement = Assert.Single(await PayoutMovementsAsync(h));
        Assert.Equal(-60_000m, movement.Amount);          // chiqim — manfiy
        Assert.Equal(sale.SaleNumber, movement.RefNumber);
    }

    [Fact]
    public async Task Payout_uses_cost_times_quantity_not_unit_cost()
    {
        using var h = new TestHarness(Market);
        // 3 dona × 60 000 = 180 000 qo'shniga.
        var sale = await SeedSaleAsync(h, Guid.NewGuid(), externalCost: 60_000m, externalPrice: 80_000m, externalQty: 3m);

        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 340_000));

        Assert.Equal(160_000m, await BalanceAsync(h));     // 340k − 180k
        Assert.Equal(-180_000m, Assert.Single(await PayoutMovementsAsync(h)).Amount);
    }

    [Fact]
    public async Task Payout_is_recorded_once_across_partial_payments()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid(), customerId: Guid.NewGuid());
        var svc = h.NewSalePaymentService();

        // Birinchi qisman to'lov sotuvni Draft'dan chiqaradi (Debt) — chiqim shunda.
        await svc.AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 100_000));
        // Qolgani keyinroq to'lanadi — chiqim TAKROR yozilmasligi kerak.
        await svc.AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 80_000));

        Assert.Single(await PayoutMovementsAsync(h));
        Assert.Equal(120_000m, await BalanceAsync(h));     // 180k tushdi, 60k chiqdi
    }

    [Fact]
    public async Task Payout_happens_on_debt_sale_too()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid(), customerId: Guid.NewGuid());

        // Mijoz butunlay qarzga oldi — kassaga bir tiyin tushmadi, lekin qo'shniga
        // pul allaqachon berilgan.
        var result = await h.NewSaleService().MarkSaleAsDebtAsync(sale.Id, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(await PayoutMovementsAsync(h));
        Assert.Equal(-60_000m, await BalanceAsync(h));
    }

    [Fact]
    public async Task Cancelling_a_finalized_sale_returns_the_payout_to_the_till()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid());

        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 180_000));
        var cancel = await h.NewSaleReversalService().CancelSaleAsync(sale.Id, Guid.NewGuid());

        Assert.True(cancel.IsSuccess, cancel.Error);
        // Mijozning 180k'i qaytdi, qo'shnidagi 60k ham qaytdi → net nol.
        Assert.Equal(0m, await BalanceAsync(h));
        Assert.Equal(2, (await PayoutMovementsAsync(h)).Count);   // chiqim + qaytarish
    }

    [Fact]
    public async Task Cancelling_a_draft_writes_no_payout_at_all()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid());

        // Qoralama hech qachon yakunlanmagan — chiqim yozilmagan edi, demak
        // "qaytarish" ham yozilmasligi kerak (jurnal toza qoladi).
        var cancel = await h.NewSaleReversalService().CancelSaleAsync(sale.Id, Guid.NewGuid());

        Assert.True(cancel.IsSuccess, cancel.Error);
        Assert.Empty(await PayoutMovementsAsync(h));
    }

    [Fact]
    public async Task Sale_without_external_lines_touches_nothing()
    {
        using var h = new TestHarness(Market);
        var sale = await SeedSaleAsync(h, Guid.NewGuid());
        // Tashqi qatorni olib tashlaymiz — oddiy sotuv qoladi.
        h.Db.SaleItems.RemoveRange(await h.Db.SaleItems.IgnoreQueryFilters().Where(i => i.IsExternal).ToListAsync());
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 100_000));

        Assert.Empty(await PayoutMovementsAsync(h));
        Assert.Equal(100_000m, await BalanceAsync(h));
    }

    [Fact]
    public async Task Shift_expects_the_payout_to_be_gone_from_the_drawer()
    {
        using var h = new TestHarness(Market);
        var seller = new User
        {
            Id = Guid.NewGuid(), FullName = "Kassir", Username = "kassir", PasswordHash = "x",
            Role = Role.Seller, IsActive = true, MarketId = Market,
        };
        h.Db.Users.Add(seller);
        var shift = new Shift
        {
            Id = Guid.NewGuid(), UserId = seller.Id, MarketId = Market,
            OpenedAt = DateTime.UtcNow.AddHours(-2), OpeningCash = 50_000m,
        };
        h.Db.Shifts.Add(shift);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var sale = await SeedSaleAsync(h, seller.Id, shiftId: shift.Id);
        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 180_000));

        var dto = await h.NewShiftService().GetCurrentShiftAsync(seller.Id);

        Assert.NotNull(dto);
        Assert.Equal(180_000m, dto!.CashIn);
        Assert.Equal(60_000m, dto.ExternalPayouts);
        // 50 000 + 180 000 − 0 − 60 000. Aynan shu yashikda turgan pul.
        Assert.Equal(170_000m, dto.ExpectedCash);
        Assert.Equal(dto.ExpectedCash, await BalanceAsync(h) + shift.OpeningCash);
    }

    [Fact]
    public async Task Cancelled_sale_drops_out_of_the_shift_reconciliation()
    {
        using var h = new TestHarness(Market);
        var seller = new User
        {
            Id = Guid.NewGuid(), FullName = "Kassir", Username = "kassir2", PasswordHash = "x",
            Role = Role.Seller, IsActive = true, MarketId = Market,
        };
        h.Db.Users.Add(seller);
        var shift = new Shift
        {
            Id = Guid.NewGuid(), UserId = seller.Id, MarketId = Market,
            OpenedAt = DateTime.UtcNow.AddHours(-2), OpeningCash = 50_000m,
        };
        h.Db.Shifts.Add(shift);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var sale = await SeedSaleAsync(h, seller.Id, shiftId: shift.Id);
        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Cash", 180_000));
        await h.NewSaleReversalService().CancelSaleAsync(sale.Id, Guid.NewGuid());

        var dto = await h.NewShiftService().GetCurrentShiftAsync(seller.Id);

        // Sverka sotuvning O'ZIDAN hosila: bekor qilingan sotuv oynadan chiqib
        // ketadi, shuning uchun chiqim ham o'z-o'zidan nolga tushadi. Kassada
        // ham −60 000 / +60 000 bo'lib nolga keldi — ikki tomon mos.
        Assert.Equal(0m, dto!.ExternalPayouts);

        // Bekor qilish qoplovchi manfiy to'lov qatorini yozadi, shuning uchun
        // cashIn ham nolga qaytadi va sverka yashikdagi haqiqiy pulga tushadi.
        Assert.Equal(0m, dto.CashIn);
        Assert.Equal(50_000m, dto.ExpectedCash);       // faqat boshlang'ich naqd
        Assert.Equal(0m, await BalanceAsync(h));
    }

    [Fact]
    public async Task Cancelling_a_card_sale_clears_it_from_the_cashless_total_too()
    {
        using var h = new TestHarness(Market);
        var seller = new User
        {
            Id = Guid.NewGuid(), FullName = "Kassir", Username = "kassir3", PasswordHash = "x",
            Role = Role.Seller, IsActive = true, MarketId = Market,
        };
        h.Db.Users.Add(seller);
        var shift = new Shift
        {
            Id = Guid.NewGuid(), UserId = seller.Id, MarketId = Market,
            OpenedAt = DateTime.UtcNow.AddHours(-2), OpeningCash = 50_000m,
        };
        h.Db.Shifts.Add(shift);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var sale = await SeedSaleAsync(h, seller.Id, shiftId: shift.Id);
        await h.NewSalePaymentService().AddPaymentAsync(sale.Id, new AddPaymentDto("Terminal", 180_000));
        await h.NewSaleReversalService().CancelSaleAsync(sale.Id, Guid.NewGuid());

        var dto = await h.NewShiftService().GetCurrentShiftAsync(seller.Id);

        // Karta ham to'lovlar jadvalidan yig'iladi — u ham tozalanishi kerak.
        // Yashiq esa tegilmaydi: karta puli bank orqali qaytadi.
        Assert.Equal(0m, dto!.CardIn);
        Assert.Equal(50_000m, dto.ExpectedCash);
    }
}
