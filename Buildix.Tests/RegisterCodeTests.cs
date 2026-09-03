using Buildix.Application.DTOs;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Chek QAYSI kassada urilgani.
///
/// <para><b>Nima uchun kerak.</b> Do'konda ikkita kassa bo'lsa, ikkalasi ham
/// bitta API ga so'rov yuboradi — lokal tarmoq rejimida 2-kassaning o'z API si
/// yo'q. Server tomonida ularni ajratadigan hech narsa yo'q edi:
/// <c>SellerId</c> faqat «kim sotgan» ni aytadi. Bitta kassir kun davomida
/// ikkala kassada ham ishlashi mumkin, ikki kassir esa bitta login ostida
/// ishlashi mumkin.</para>
///
/// <para><b>Nega sarlavhada.</b> Belgi server sozlamasidan olinsa, LAN
/// rejimida har bir chek SERVERNING belgisi bilan yozilardi — 2-kassaning
/// cheklari ham. Shuning uchun u so'rovning o'zida keladi va uni har
/// kompyuterning qobig'i qo'yadi.</para>
/// </summary>
public class RegisterCodeTests
{
    private const int Market = 1;

    private static User AddSeller(TestHarness h)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), MarketId = Market, Username = "kassir", FullName = "Kassir",
            PasswordHash = "x", Role = Role.Seller, IsActive = true,
        };
        h.Db.Users.Add(user);
        return user;
    }

    /// <summary>Belgi yangi chekka yoziladi.</summary>
    [Fact]
    public async Task Belgi_chekka_yoziladi()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        h.Register.Code = "B";
        var created = await h.NewSaleService().CreateSaleAsync(new CreateSaleDto(null), seller.Id);

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal("B", created.Value.RegisterCode);

        var stored = await h.Db.Sales.IgnoreQueryFilters().FirstAsync();
        Assert.Equal("B", stored.RegisterCode);
    }

    /// <summary>
    /// Belgisiz kassa (yoki brauzerdan kirilgan) — chek belgisiz qoladi va
    /// bu XATO emas.
    /// </summary>
    [Fact]
    public async Task Belgisiz_ham_ishlaydi()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        // Sukut bo'yicha belgi yo'q — egasi telefonda ochgan holat.
        var created = await h.NewSaleService().CreateSaleAsync(new CreateSaleDto(null), seller.Id);

        Assert.True(created.IsSuccess, created.Error);
        Assert.Null(created.Value.RegisterCode);
    }

    /// <summary>
    /// Sarlavha ISHONCHSIZ manba — u tozalanadi.
    /// </summary>
    /// <remarks>
    /// Sarlavhani har kim yozishi mumkin. Uzun matn, bo'shliq yoki tinish
    /// belgisi bazaga tushib, chek ustida va ro'yxatlarda ekranni buzardi.
    /// Shuning uchun faqat harf-raqam qabul qilinadi, uzunlik kesiladi va
    /// katta harfga keltiriladi.
    /// </remarks>
    [Theory]
    [InlineData("a", "A")]
    [InlineData("  b  ", "B")]
    [InlineData("KASSA-2", null)]        // tinish belgisi — rad etiladi
    [InlineData("ABCDEFGH", "ABCD")]     // uzunligi kesiladi
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("<b>x", null)]           // razmetka — rad etiladi
    public void Sarlavha_tozalanadi(string header, string? expected)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Request.Headers[CurrentRegisterService.HeaderName] = header;

        Assert.Equal(expected, new CurrentRegisterService(accessor).GetRegisterCode());
    }

    /// <summary>Sarlavha umuman bo'lmasa — <c>null</c>.</summary>
    [Fact]
    public void Sarlavhasiz_null_qaytadi()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        Assert.Null(new CurrentRegisterService(accessor).GetRegisterCode());
    }
}
