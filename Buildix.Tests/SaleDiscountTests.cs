using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Chegirma allaqachon to'lov qilingan chekka ham qo'yiladi — bu ataylab:
/// mijoz bilan kelishilgan chegirma ko'pincha qarz yopilgandan keyin paydo
/// bo'ladi.
///
/// <para>Lekin u jamini TO'LANGAN summadan pastga tushira olmaydi. Bu holat
/// haqiqiy nosozlik bo'lgan: chek «ortiqcha to'langan» holatga tushar va
/// undan CHIQIB BO'LMASDI — keyingi har bir to'lov urinishi «Bu savdo
/// allaqachon to'liq to'langan» degan tushunarsiz xabarga urilardi. Kassir
/// yangi chek ochyapman deb o'ylar, ekranda esa to'lanishi kerak bo'lgan
/// summa turardi.</para>
/// </summary>
public class SaleDiscountTests
{
    private static async Task<Guid> SeedAsync(
        TestHarness h, decimal unitPrice, decimal qty, decimal paidAmount, SaleStatus status)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(), Name = "Sement", MarketId = 1,
            CostPrice = unitPrice / 2, SalePrice = unitPrice, MinSalePrice = unitPrice / 2,
            Quantity = 1000, MinThreshold = 1, Unit = UnitType.Piece,
        };
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(), MarketId = 1,
            Status = status, PaidAmount = paidAmount, TotalAmount = unitPrice * qty,
        };
        h.Db.Products.Add(product);
        h.Db.Sales.Add(sale);
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = qty, SalePrice = unitPrice, CostPrice = product.CostPrice, IsExternal = false,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return sale.Id;
    }

    /// <summary>
    /// ASOSIY tuzatish: to'langan summadan pastga tushiradigan chegirma rad
    /// etiladi.
    /// </summary>
    [Fact]
    public async Task Tolangan_summadan_past_chegirma_rad_etiladi()
    {
        using var h = new TestHarness();
        // Jami 763 000, shundan 100 000 to'langan.
        var saleId = await SeedAsync(h, unitPrice: 1_000, qty: 763, paidAmount: 100_000,
                                     status: SaleStatus.Debt);

        var result = await h.NewSaleService().SetSaleDiscountAsync(saleId, 713_000, Guid.NewGuid());

        Assert.True(result.IsFailure);
        // Xabar AMALIY bo'lishi kerak: kassir qancha qo'ya olishini ko'rsin.
        Assert.Contains("663 000", result.Error);

        var sale = await h.Db.Sales.IgnoreQueryFilters().SingleAsync(s => s.Id == saleId);
        Assert.Equal(0, sale.DiscountAmount);
        Assert.Equal(763_000, sale.TotalAmount);
    }

    /// <summary>
    /// Chegarasigacha bo'lgan chegirma o'tadi — chek aynan nol qoldiq bilan
    /// yopiladi.
    /// </summary>
    [Fact]
    public async Task Tolangan_summagacha_chegirma_otadi()
    {
        using var h = new TestHarness();
        var saleId = await SeedAsync(h, unitPrice: 1_000, qty: 763, paidAmount: 100_000,
                                     status: SaleStatus.Debt);

        var result = await h.NewSaleService().SetSaleDiscountAsync(saleId, 663_000, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var sale = await h.Db.Sales.IgnoreQueryFilters().SingleAsync(s => s.Id == saleId);
        Assert.Equal(100_000, sale.TotalAmount);
        Assert.Equal(sale.PaidAmount, sale.TotalAmount);
    }

    /// <summary>
    /// To'lovsiz chekda chegirma cheklanmaydi — eski xulq buzilmagan.
    /// </summary>
    [Fact]
    public async Task Tolovsiz_chekda_chegirma_erkin()
    {
        using var h = new TestHarness();
        var saleId = await SeedAsync(h, unitPrice: 1_000, qty: 763, paidAmount: 0,
                                     status: SaleStatus.Draft);

        var result = await h.NewSaleService().SetSaleDiscountAsync(saleId, 763_000, Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var sale = await h.Db.Sales.IgnoreQueryFilters().SingleAsync(s => s.Id == saleId);
        Assert.Equal(0, sale.TotalAmount);
    }

    /// <summary>
    /// Ortiqcha to'langan chek uchun xabar ALOHIDA bo'lishi kerak.
    ///
    /// <para>Ilgari u ham «allaqachon to'liq to'langan» degan bir xil xabarga
    /// tushardi va sabab — chegirma jamini pastga tushirgani — hech qayerdan
    /// ko'rinmasdi.</para>
    /// </summary>
    [Fact]
    public async Task Ortiqcha_tolangan_chek_alohida_aytiladi()
    {
        using var h = new TestHarness();
        // Jami 50 000, to'langan 100 000 — eski nuqson qoldirgan holat.
        var saleId = await SeedAsync(h, unitPrice: 1_000, qty: 50, paidAmount: 100_000,
                                     status: SaleStatus.Debt);

        var result = await h.NewSalePaymentService()
            .AddPaymentAsync(saleId, new AddPaymentDto("Cash", 1_000));

        Assert.True(result.IsFailure);
        Assert.Contains("ortiqcha", result.Error, StringComparison.OrdinalIgnoreCase);
        // Qaytariladigan summa aytilishi kerak.
        Assert.Contains("50 000", result.Error);
    }
}
