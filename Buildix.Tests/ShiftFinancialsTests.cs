using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Smena moliyasi — ro'yxatda va yakka holda BIR XIL chiqishi kerak.
///
/// <para>Ilgari har bir smena uchun o'n beshta ketma-ket so'rov bajarilardi va
/// ular halqa ichida edi: ellikta smenali ro'yxat 750 ta so'rov degani, panel
/// esa uni har daqiqada qayta so'rardi. Hisob to'rtta guruhli so'rovga
/// ko'chirildi, natijalar esa xotirada taqsimlanadi.</para>
///
/// <para>Bu sinovlar aynan shu ko'chirishni qo'riqlaydi: raqamlar yakka
/// smenani o'qiydigan yo'l (joriy smena, smenani yopish) bilan ro'yxat
/// yo'lida bir xil bo'lishi shart. Ikki kassir, aralash to'lov, qaytarish,
/// naqd yechish va qo'shni do'kondan olingan tovar — hammasi bir vaqtda,
/// chunki xatolar aynan chegaralarda tug'iladi.</para>
/// </summary>
public class ShiftFinancialsTests
{
    private const int Market = 1;

    private static User AddUser(TestHarness h, string name)
    {
        var u = new User
        {
            Id = Guid.NewGuid(), FullName = name, Username = name,
            PasswordHash = "x", Role = Role.Seller, IsActive = true, MarketId = Market,
        };
        h.Db.Users.Add(u);
        return u;
    }

    private static Shift AddShift(TestHarness h, Guid userId, DateTime openedAt, DateTime? closedAt, decimal opening)
    {
        var s = new Shift
        {
            Id = Guid.NewGuid(), UserId = userId, MarketId = Market,
            OpenedAt = openedAt, ClosedAt = closedAt, OpeningCash = opening,
            ReconStatus = closedAt is null ? CashShiftStatus.Open : CashShiftStatus.Balanced,
        };
        h.Db.Shifts.Add(s);
        return s;
    }

    private static Sale AddSale(
        TestHarness h, Guid sellerId, DateTime at, decimal total, decimal paid, SaleStatus status)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = Market, SellerId = sellerId, CreatedAt = at,
            TotalAmount = total, PaidAmount = paid, Status = status,
        };
        h.Db.Sales.Add(sale);
        return sale;
    }

    private static void AddPayment(
        TestHarness h, Sale sale, PaymentType type, decimal amount, DateTime at, Guid? collectedBy = null)
        => h.Db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, MarketId = Market,
            PaymentType = type, Amount = amount, CreatedAt = at, CollectedByUserId = collectedBy,
        });

    /// <summary>
    /// Ikki kassir, uchta smena, hamma turdagi harakat — ro'yxat va yakka
    /// o'qish AYNAN bir xil raqam berishi kerak.
    /// </summary>
    [Fact]
    public async Task Royxat_va_yakka_hisob_bir_xil()
    {
        using var h = new TestHarness(Market);
        var t0 = new DateTime(2026, 5, 12, 3, 0, 0, DateTimeKind.Utc);

        var ali = AddUser(h, "Ali");
        var vali = AddUser(h, "Vali");

        var aliShift = AddShift(h, ali.Id, t0, t0.AddHours(8), opening: 100_000);
        var valiShift = AddShift(h, vali.Id, t0.AddHours(1), t0.AddHours(9), opening: 50_000);
        // Ali ning ikkinchi, YOPILMAGAN smenasi — oynasi ochiq qoladi.
        var aliOpen = AddShift(h, ali.Id, t0.AddHours(10), null, opening: 20_000);

        // Ali: naqd chek + aralash chek (terminal & click) + qaytarish.
        var s1 = AddSale(h, ali.Id, t0.AddHours(1), 300_000, 300_000, SaleStatus.Paid);
        AddPayment(h, s1, PaymentType.Cash, 300_000, t0.AddHours(1));

        var s2 = AddSale(h, ali.Id, t0.AddHours(2), 500_000, 500_000, SaleStatus.Paid);
        AddPayment(h, s2, PaymentType.Terminal, 200_000, t0.AddHours(2));
        AddPayment(h, s2, PaymentType.Click, 300_000, t0.AddHours(2));

        var s3 = AddSale(h, ali.Id, t0.AddHours(3), 80_000, 80_000, SaleStatus.Paid);
        AddPayment(h, s3, PaymentType.Cash, 80_000, t0.AddHours(3));
        AddPayment(h, s3, PaymentType.Cash, -80_000, t0.AddHours(4));   // qaytarildi

        // Ali: qarzga sotuv — qoldig'i smenaga yoziladi.
        AddSale(h, ali.Id, t0.AddHours(5), 400_000, 100_000, SaleStatus.Debt);

        // Ali: qo'shni do'kondan olingan tovar — kassadan chiqadi.
        var s5 = AddSale(h, ali.Id, t0.AddHours(6), 150_000, 150_000, SaleStatus.Paid);
        AddPayment(h, s5, PaymentType.Cash, 150_000, t0.AddHours(6));
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = s5.Id, IsExternal = true,
            Quantity = 2, ExternalCostPrice = 30_000, SalePrice = 75_000,
        });

        // Vali: o'z cheki + Ali ning qarzini KEYIN yig'adi (CollectedByUserId).
        var s6 = AddSale(h, vali.Id, t0.AddHours(2), 90_000, 90_000, SaleStatus.Paid);
        AddPayment(h, s6, PaymentType.Cash, 90_000, t0.AddHours(2));
        AddPayment(h, s1, PaymentType.Cash, 0m, t0.AddHours(3), collectedBy: vali.Id);

        // Ali: naqd yechish.
        h.Db.CashWithdrawals.Add(new CashWithdrawal
        {
            Id = Guid.NewGuid(), MarketId = Market, UserId = ali.Id, Amount = 70_000,
            WithdrawType = "cash", ApprovalStatus = WithdrawalApprovalStatus.NotRequired,
            WithdrawalDate = t0.AddHours(4),
        });

        // Oynadan TASHQARIDA — hech bir smenaga tushmasligi kerak.
        var late = AddSale(h, ali.Id, t0.AddHours(30), 999_000, 999_000, SaleStatus.Paid);
        AddPayment(h, late, PaymentType.Cash, 999_000, t0.AddHours(30));

        await h.Db.SaveChangesAsync();

        var service = h.NewShiftService();

        // Ro'yxat yo'li — bitta guruhli hisob.
        var list = await service.GetMarketShiftsAsync(limit: 50);
        var byId = list.ToDictionary(x => x.Id);

        // Yakka yo'l — joriy smena (Ali ning ochiq smenasi).
        var current = await service.GetCurrentShiftAsync(ali.Id);

        Assert.NotNull(current);
        Assert.Equal(aliOpen.Id, current!.Id);

        // Ochiq smena ikkala yo'lda ham bir xil.
        var fromList = byId[aliOpen.Id];
        Assert.Equal(fromList.CashIn, current.CashIn);
        Assert.Equal(fromList.ExpectedCash, current.ExpectedCash);
        Assert.Equal(fromList.CheckCount, current.CheckCount);

        // ── Ali ning yopilgan smenasi: raqamlar QO'LDA hisoblangan ──────
        var a = byId[aliShift.Id];
        // Naqd: 300 000 + 80 000 − 80 000 + 150 000 + 0 (Vali yiqqani emas)
        Assert.Equal(450_000m, a.CashIn);
        // Naqdsiz: terminal 200 000 + click 300 000
        Assert.Equal(500_000m, a.CardIn);
        Assert.Equal(300_000m, a.ClickIn);
        Assert.Equal(200_000m, a.TerminalIn);
        // Qaytarish faqat ko'rsatish uchun
        Assert.Equal(80_000m, a.ReturnAmount);
        Assert.Equal(1, a.ReturnCount);
        // Qarz qoldig'i
        Assert.Equal(300_000m, a.DebtIn);
        Assert.Equal(1, a.DebtCount);
        // Qo'shni do'kon tovari: 2 × 30 000
        Assert.Equal(60_000m, a.ExternalPayouts);
        // Kutilgan naqd: 100 000 + 450 000 − 70 000 − 60 000
        Assert.Equal(420_000m, a.ExpectedCash);
        // Cheklar: s1, s2, s3, qarz, s5 — beshta (oynadan tashqaridagisi yo'q)
        Assert.Equal(5, a.CheckCount);

        // ── Vali: Ali ning chekidan yiqqan puli UNGA tegishli ───────────
        var v = byId[valiShift.Id];
        Assert.Equal(90_000m, v.CashIn);
        Assert.Equal(50_000m + 90_000m, v.ExpectedCash);
        // Sotuvlar SellerId bo'yicha: faqat o'z cheki
        Assert.Equal(1, v.CheckCount);
    }

    /// <summary>
    /// Avans harakati smenaning «Qaytarishlar» raqamiga TUSHMAYDI.
    /// </summary>
    /// <remarks>
    /// <para>Chek kichrayganda mijozning avansi manfiy <c>Credit</c> qatori
    /// bilan qaytariladi. Qaytarishlar faqat summa belgisi bo'yicha
    /// ajratilardi, ya'ni bu harakat «Возвраты» ga tushar va smena
    /// yopilishida kassir kassadan chiqmagan pulni izlab qolardi. Naqd va
    /// naqdsiz yig'indilar <c>Credit</c> ni allaqachon chiqarib
    /// tashlaydi.</para>
    /// </remarks>
    [Fact]
    public async Task Avans_harakati_qaytarish_deb_sanalmaydi()
    {
        using var h = new TestHarness(Market);
        var t0 = new DateTime(2026, 5, 12, 3, 0, 0, DateTimeKind.Utc);

        var ali = AddUser(h, "Ali");
        var shift = AddShift(h, ali.Id, t0, t0.AddHours(8), opening: 0);

        // Naqd chek + qo'llangan avans, keyin chek kichraydi va avans qaytadi.
        var sale = AddSale(h, ali.Id, t0.AddHours(1), 200_000, 200_000, SaleStatus.Paid);
        AddPayment(h, sale, PaymentType.Cash, 100_000, t0.AddHours(1));
        AddPayment(h, sale, PaymentType.Credit, 100_000, t0.AddHours(1));
        AddPayment(h, sale, PaymentType.Credit, -100_000, t0.AddHours(2));

        await h.Db.SaveChangesAsync();

        var stats = (await h.NewShiftService().GetMarketShiftsAsync(limit: 50))
            .Single(x => x.Id == shift.Id);

        Assert.Equal(0m, stats.ReturnAmount);
        Assert.Equal(0, stats.ReturnCount);
        // Naqd o'z holicha: avans kassaga tushmagan ham, undan chiqmagan ham.
        Assert.Equal(100_000m, stats.CashIn);
    }

    /// <summary>
    /// Bo'sh ro'yxat — so'rovlar umuman yuborilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Bosh_royxat_yiqilmaydi()
    {
        using var h = new TestHarness(Market);
        Assert.Empty(await h.NewShiftService().GetMarketShiftsAsync(limit: 50));
    }

    /// <summary>
    /// Smenasiz kassirda joriy smena yo'q — yakka yo'l ham yiqilmaydi.
    /// </summary>
    [Fact]
    public async Task Smenasiz_kassirda_joriy_smena_yoq()
    {
        using var h = new TestHarness(Market);
        var user = AddUser(h, "Yolg'iz");
        await h.Db.SaveChangesAsync();

        Assert.Null(await h.NewShiftService().GetCurrentShiftAsync(user.Id));
    }
}
