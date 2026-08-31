using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Mijozning avansi — do'konda QOLGAN puli.
///
/// <para>Bu sinovlar bazadan topilgan haqiqiy holatdan keyin yozildi: mijozda
/// 1 590 000 so'm «avans» turgan, u esa avvalgi qaytarishdan kelib chiqqan
/// edi — pul mijozga naqd berib yuborilgan bo'lsa ham. Ya'ni do'kon bir
/// pulni ikki marta to'lardi.</para>
/// </summary>
public class CustomerCreditTests
{
    private static CustomerService NewService(TestHarness h) =>
        new(h.UnitOfWork, h.Db, h.Market, Substitute.For<IHttpContextAccessor>(), h.Clock);

    /// <summary>Mijoz + uning bitta sotuvi.</summary>
    private static (Guid CustomerId, Guid SaleId) Seed(TestHarness h)
    {
        var customer = new Customer { Id = Guid.NewGuid(), MarketId = 1, Phone = "+998900000000" };
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = 1, CustomerId = customer.Id,
            SellerId = Guid.NewGuid(), SaleNumber = 1,
            TotalAmount = 100_000, PaidAmount = 100_000, Status = SaleStatus.Paid,
        };
        h.Db.Customers.Add(customer);
        h.Db.Sales.Add(sale);
        h.Db.SaveChanges();
        return (customer.Id, sale.Id);
    }

    private static void AddPayment(TestHarness h, Guid saleId, PaymentType type, decimal amount) =>
        h.Db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(), SaleId = saleId, MarketId = 1,
            PaymentType = type, Amount = amount, CreatedAt = DateTime.UtcNow,
        });

    /// <summary>
    /// NAQD qaytarilgan pul avans BO'LMAYDI.
    /// </summary>
    /// <remarks>
    /// Qaytarish pulni jismonan chiqaradi: manfiy qator yoziladi VA kassa
    /// qoldig'i kamayadi. O'sha summa avans bo'lib qaytsa, mijoz uni ikkinchi
    /// marta ishlatardi.
    /// </remarks>
    [Fact]
    public async Task Naqd_qaytarish_avans_yaratmaydi()
    {
        using var h = new TestHarness();
        var (customerId, saleId) = Seed(h);
        AddPayment(h, saleId, PaymentType.Cash, 100_000);
        AddPayment(h, saleId, PaymentType.Cash, -100_000);   // qaytarildi
        await h.Db.SaveChangesAsync();

        Assert.Equal(0m, await NewService(h).GetAvailableCreditAsync(customerId));
    }

    /// <summary>Terminal va o'tkazma ham xuddi shunday — pul bank orqali qaytadi.</summary>
    [Theory]
    [InlineData(PaymentType.Terminal)]
    [InlineData(PaymentType.Transfer)]
    public async Task Bank_orqali_qaytarish_ham_avans_yaratmaydi(PaymentType type)
    {
        using var h = new TestHarness();
        var (customerId, saleId) = Seed(h);
        AddPayment(h, saleId, type, 250_000);
        AddPayment(h, saleId, type, -250_000);
        await h.Db.SaveChangesAsync();

        Assert.Equal(0m, await NewService(h).GetAvailableCreditAsync(customerId));
    }

    /// <summary>
    /// Do'konda QOLDIRILGAN pul (manfiy Credit qatori) avans bo'ladi.
    /// </summary>
    /// <remarks>
    /// Bunday qator hozircha yozilmaydi — qaytarish oynasida «avansga»
    /// usuli yo'q. Hisob esa usul qo'shilishiga tayyor turishi kerak.
    /// </remarks>
    [Fact]
    public async Task Dokonda_qoldirilgan_pul_avans_boladi()
    {
        using var h = new TestHarness();
        var (customerId, saleId) = Seed(h);
        AddPayment(h, saleId, PaymentType.Credit, -300_000);
        await h.Db.SaveChangesAsync();

        Assert.Equal(300_000m, await NewService(h).GetAvailableCreditAsync(customerId));
    }

    /// <summary>Sarflangan avans qaytib chiqmaydi.</summary>
    [Fact]
    public async Task Sarflangan_avans_qayta_hisoblanmaydi()
    {
        using var h = new TestHarness();
        var (customerId, saleId) = Seed(h);
        AddPayment(h, saleId, PaymentType.Credit, -300_000);
        AddPayment(h, saleId, PaymentType.Credit, 200_000);   // keyingi xaridga o'tdi
        await h.Db.SaveChangesAsync();

        Assert.Equal(100_000m, await NewService(h).GetAvailableCreditAsync(customerId));
    }

    /// <summary>Avansi yo'q mijozda hech narsa qo'llanmaydi.</summary>
    [Fact]
    public async Task Tolovsiz_mijozda_avans_nol()
    {
        using var h = new TestHarness();
        var (customerId, _) = Seed(h);

        Assert.Equal(0m, await NewService(h).GetAvailableCreditAsync(customerId));
    }
}
