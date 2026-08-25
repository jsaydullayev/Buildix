using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// <c>UpdatedAt</c> — bulut bilan sinxronizatsiyaning suv belgisi. Bu yerdagi
/// testlar mexanizmning o'zini tekshiradi, chunki uning buzilishi hech qanday
/// belgi bermaydi: kod ishlaydi, boshqa testlar o'tadi, faqat bulutdagi
/// ma'lumot jimgina eskirib boradi.
///
/// <para><b>Vaqt qo'lda suriladi</b> (<see cref="TestHarness.DbClock"/>).
/// Birinchi urinishda tizim soati ishlatilgan va testlar YIQILMASDAN o'tgan
/// edi — lekin aslida hech narsani tekshirmasdi: yozuv saqlangan zahoti vaqt
/// baribir «hozir» bo'lib qolardi va «eskisidan katta» sharti har doim
/// bajarilardi.</para>
/// </summary>
public class UpdatedAtTrackingTests
{
    private static CreateProductDto NewProduct(string name = "Cement") =>
        new(name, false, 50_000, 40_000, 5, null, 1, 100, false, 30_000);

    private static Task<Product> ReadAsync(TestHarness h, Guid id) =>
        h.Db.Products.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == id);

    [Fact]
    public async Task Yangi_yozuv_yaratilgan_vaqtni_oladi()
    {
        using var h = new TestHarness(marketId: 3);
        var expected = h.DbClock.GetUtcNow().UtcDateTime;

        var created = await h.NewProductService().CreateProductAsync(NewProduct(), sellerId: null);

        Assert.True(created.IsSuccess, created.Error);
        var product = await ReadAsync(h, created.Value.Id);
        Assert.Equal(expected, product.UpdatedAt);
    }

    /// <summary>
    /// Asosiy regressiya testi. <c>ProductService</c> ilgari bu maydonga
    /// umuman tegmasdi: tovar narxi o'zgarardi, <c>UpdatedAt</c> esa eski
    /// qolardi — ya'ni yangi narx bulutga hech qachon yetib bormasdi.
    /// </summary>
    [Fact]
    public async Task Narx_ozgarganda_vaqt_yangilanadi()
    {
        using var h = new TestHarness(marketId: 3);
        var created = await h.NewProductService().CreateProductAsync(NewProduct(), sellerId: null);
        var yaratilgan = (await ReadAsync(h, created.Value.Id)).UpdatedAt;

        h.DbClock.Advance(TimeSpan.FromHours(2));
        var patched = await h.NewProductService().PatchProductAsync(
            created.Value.Id, new ProductPatchDto(SalePrice: 77_000), actorUserId: Guid.NewGuid());

        Assert.True(patched.IsSuccess, patched.Error);
        var after = await ReadAsync(h, created.Value.Id);
        Assert.Equal(77_000, after.SalePrice);
        Assert.Equal(yaratilgan.AddHours(2), after.UpdatedAt);
    }

    /// <summary>
    /// Yumshoq o'chirish — bu ham o'zgarish. Vaqt yangilanmasa bulut
    /// o'chirilgan yozuvni hech qachon ko'rmaydi va u o'sha yerda tirik
    /// bo'lib qolaveradi.
    /// </summary>
    [Fact]
    public async Task Yumshoq_ochirish_vaqtni_yangilaydi()
    {
        using var h = new TestHarness(marketId: 3);
        var created = await h.NewProductService().CreateProductAsync(NewProduct(), sellerId: null);
        var yaratilgan = (await ReadAsync(h, created.Value.Id)).UpdatedAt;

        h.DbClock.Advance(TimeSpan.FromHours(5));
        var tracked = await h.Db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == created.Value.Id);
        tracked.IsDeleted = true;
        tracked.DeletedAt = h.DbClock.GetUtcNow().UtcDateTime;
        await h.Db.SaveChangesAsync();

        var after = await ReadAsync(h, created.Value.Id);
        Assert.True(after.IsDeleted);
        Assert.Equal(yaratilgan.AddHours(5), after.UpdatedAt);
    }

    /// <summary>
    /// Bitta saqlash — bitta vaqt. Yozuvlar orasida mikrosoniya farqi bo'lsa,
    /// suv belgisi aynan shu farqqa tushgan yozuvni o'tkazib yuborishi mumkin.
    /// </summary>
    [Fact]
    public async Task Birga_saqlangan_yozuvlar_bitta_vaqt_oladi()
    {
        using var h = new TestHarness(marketId: 3);

        h.Db.Products.Add(new Product { Id = Guid.NewGuid(), Name = "A", MarketId = 3, Unit = UnitType.Piece });
        h.Db.Products.Add(new Product { Id = Guid.NewGuid(), Name = "B", MarketId = 3, Unit = UnitType.Piece });
        h.Db.Customers.Add(new Customer { Id = Guid.NewGuid(), FullName = "Mijoz", MarketId = 3 });
        await h.Db.SaveChangesAsync();

        var stamps = (await h.Db.Products.IgnoreQueryFilters().AsNoTracking()
                .Select(p => p.UpdatedAt).ToListAsync())
            .Concat(await h.Db.Customers.IgnoreQueryFilters().AsNoTracking()
                .Select(c => c.UpdatedAt).ToListAsync())
            .Distinct()
            .ToList();

        Assert.Single(stamps);
    }

    /// <summary>
    /// Tegilmagan yozuvning vaqti o'zgarmaydi — aks holda har saqlashda butun
    /// baza «o'zgargan» bo'lib chiqar va sinxronizatsiya har safar hamma
    /// narsani qaytadan yuborardi.
    /// </summary>
    [Fact]
    public async Task Tegilmagan_yozuvning_vaqti_saqlanadi()
    {
        using var h = new TestHarness(marketId: 3);
        var first = await h.NewProductService().CreateProductAsync(NewProduct("Birinchi"), sellerId: null);
        var boshlangich = (await ReadAsync(h, first.Value.Id)).UpdatedAt;

        h.DbClock.Advance(TimeSpan.FromDays(3));
        await h.NewProductService().CreateProductAsync(NewProduct("Ikkinchi"), sellerId: null);

        var after = await ReadAsync(h, first.Value.Id);
        Assert.Equal(boshlangich, after.UpdatedAt);
    }

    /// <summary>
    /// Kassa registri — nomi <c>LastUpdated</c> dan <c>UpdatedAt</c> ga
    /// o'zgargan yagona jadval. Qo'lda yozilgan belgilar olib tashlangani
    /// uchun bu yerda haqiqatan markaz ishlayotganini tekshirish kerak:
    /// ishlamasa, kassa qoldig'ining o'zgarishi bulutga yetib bormasdi.
    /// </summary>
    [Fact]
    public async Task Kassa_qoldigi_ozgarganda_vaqt_yangilanadi()
    {
        using var h = new TestHarness(marketId: 3);
        var register = new CashRegister { Id = Guid.NewGuid(), MarketId = 3, CurrentBalance = 0m };
        h.Db.CashRegisters.Add(register);
        await h.Db.SaveChangesAsync();
        var yaratilgan = register.UpdatedAt;

        h.DbClock.Advance(TimeSpan.FromMinutes(40));
        register.CurrentBalance = 250_000m;
        await h.Db.SaveChangesAsync();

        Assert.Equal(yaratilgan.AddMinutes(40), register.UpdatedAt);
    }
}
