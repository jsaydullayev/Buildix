using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Chek raqami — moliyaviy hujjatning izi.
///
/// <para><b>Nima muammo.</b> Ikkita kassa o'z bazasi bilan ishlaganda ular
/// bir-birining raqamini KO'RMAYDI: ikkalasi ham o'z bazasining maksimumidan
/// hisoblaydi va bir xil son beradi. Bulutda birlashganda ikkita «№101»
/// yonma-yon yotardi — qaytarish esa aynan raqam bo'yicha izlanadi va
/// «101 ni qaytar» degan so'rov ikki xil chekka to'g'ri kelardi.</para>
///
/// <para><b>Yechim.</b> Chekning takrorlanmas belgisi — raqamning o'zi emas,
/// «kassa + raqam» juftligi. Raqam AJRATISH esa o'zgarmadi: bitta bazali
/// do'konda raqamlar allaqachon to'qnashmaydi, mustaqil bazalarda esa uni
/// o'zgartirish hech narsa qo'shmaydi. Takrorlanmaslikni juftlik va
/// bazadagi yagona indeks ta'minlaydi.</para>
/// </summary>
public class SaleNumberingTests
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

    /// <summary>
    /// Belgisiz do'konda raqamlash AVVALGIDEK — 1, 2, 3.
    /// </summary>
    /// <remarks>
    /// Eng muhim tekshiruv: ko'p kassali rejimga tayyorgarlik bitta kassali
    /// do'konning chek raqamlariga TEGMASLIGI kerak. Raqam — moliyaviy
    /// hujjatning izi va uning uzilishi ma'lumot yo'qolishiga o'xshab
    /// ko'rinardi.
    /// </remarks>
    [Fact]
    public async Task Belgisiz_dokonda_raqamlash_ozgarmaydi()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var sales = h.NewSaleService();
        var first = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);
        var second = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);
        var third = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);

        Assert.Equal(1, first.Value.SaleNumber);
        Assert.Equal(2, second.Value.SaleNumber);
        Assert.Equal(3, third.Value.SaleNumber);
        Assert.All(new[] { first, second, third }, s => Assert.Null(s.Value.RegisterCode));
    }

    /// <summary>
    /// Bitta bazaga ulangan ikki kassa — raqamlar YAGONA ketma-ketlikda.
    /// </summary>
    /// <remarks>
    /// Lokal tarmoq rejimida 2-kassaning o'z bazasi yo'q, ya'ni raqamni
    /// bitta ajratuvchi beradi va to'qnashuv umuman mumkin emas. Har kassaga
    /// alohida hisob berish bu yerda faqat zarar qilardi: ekranda ikkita
    /// «№2» paydo bo'lardi.
    /// </remarks>
    [Fact]
    public async Task Bitta_bazadagi_ikki_kassa_yagona_ketma_ketlik()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var sales = h.NewSaleService();

        h.Register.Code = "A";
        var a1 = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);
        h.Register.Code = "B";
        var b1 = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);
        h.Register.Code = "A";
        var a2 = await sales.CreateSaleAsync(new CreateSaleDto(null), seller.Id);

        Assert.Equal(1, a1.Value.SaleNumber);
        Assert.Equal(2, b1.Value.SaleNumber);
        Assert.Equal(3, a2.Value.SaleNumber);

        Assert.Equal("A", a1.Value.RegisterCode);
        Assert.Equal("B", b1.Value.RegisterCode);
    }

    /// <summary>
    /// «Kassa + raqam» juftligi — chekning takrorlanmas belgisi.
    /// </summary>
    /// <remarks>
    /// <para>Mustaqil bazalar rejimini taqlid qiladi: ikkala kassa ham
    /// mustaqil ravishda «№101» bergan va yozuvlar bir joyda uchrashgan.
    /// Raqam bir xil, lekin ular IKKI XIL chek — va ularni ajratadigan
    /// yagona narsa kassa belgisi.</para>
    ///
    /// <para>Bazada bu (MarketId, RegisterCode, SaleNumber) bo'yicha yagona
    /// indeks bilan qo'riqlanadi: belgisiz ikkita bir xil raqam yozib
    /// bo'lmaydi va to'qnashuv JIMGINA qabul qilinmaydi.</para>
    /// </remarks>
    [Fact]
    public async Task Kassa_va_raqam_juftligi_chekni_ajratadi()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);

        // Ikki kassadan kelgan, bir xil raqamli cheklar.
        h.Db.Sales.AddRange(
            new Sale
            {
                Id = Guid.NewGuid(), MarketId = Market, SellerId = seller.Id,
                SaleNumber = 101, RegisterCode = "A", Status = SaleStatus.Paid,
            },
            new Sale
            {
                Id = Guid.NewGuid(), MarketId = Market, SellerId = seller.Id,
                SaleNumber = 101, RegisterCode = "B", Status = SaleStatus.Paid,
            });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var stored = await h.Db.Sales.IgnoreQueryFilters()
            .Where(s => s.SaleNumber == 101).ToListAsync();

        Assert.Equal(2, stored.Count);
        // Juftlik takrorlanmaydi — chek qaysi kassaniki ekani aniq.
        Assert.Equal(
            2,
            stored.Select(s => (s.RegisterCode, s.SaleNumber)).Distinct().Count());
    }

    /// <summary>
    /// Eski qarzning texnik qatori raqamsiz qoladi va indeksni yiqitmaydi.
    /// </summary>
    /// <remarks>
    /// <c>IsOpeningBalance</c> qatorlari savdo emas — ular mijozning eski
    /// qarzini yozib qo'yish uchun yaratiladi va chek raqami olmaydi
    /// (<c>SaleNumber = 0</c>). Bunday mijoz bittadan ko'p bo'lishi odatiy
    /// hol, ya'ni indeks 0 li qatorlarni CHIQARIB tashlashi shart —
    /// aks holda ikkinchi shunday mijoz paydo bo'lishi bilan u umuman
    /// qurilmasdi.
    /// </remarks>
    [Fact]
    public async Task Eski_qarz_qatorlari_raqamsiz_bolaveradi()
    {
        using var h = new TestHarness(Market);
        var seller = AddSeller(h);

        for (var i = 0; i < 3; i++)
        {
            h.Db.Sales.Add(new Sale
            {
                Id = Guid.NewGuid(), MarketId = Market, SellerId = seller.Id,
                SaleNumber = 0, IsOpeningBalance = true, Status = SaleStatus.Debt,
            });
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var zeros = await h.Db.Sales.IgnoreQueryFilters()
            .CountAsync(s => s.SaleNumber == 0);
        Assert.Equal(3, zeros);

        // Keyingi HAQIQIY savdo 1 dan boshlanadi: 0 raqam emas.
        var created = await h.NewSaleService()
            .CreateSaleAsync(new CreateSaleDto(null), seller.Id);
        Assert.Equal(1, created.Value.SaleNumber);
    }
}
