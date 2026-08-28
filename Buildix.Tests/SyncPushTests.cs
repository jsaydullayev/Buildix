using System.Text.Json;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buildix.Tests;

/// <summary>
/// Bulut tomoni: do'kondan kelgan yozuvlarni qabul qilish. Bu yerdagi eng
/// muhim tekshiruv — bir do'kon boshqasining ma'lumotini buza olmasligi.
/// </summary>
public class SyncPushTests
{
    private static SyncPushService NewService(TestHarness h) =>
        new(h.Db, h.UnitOfWork, NullLogger<SyncPushService>.Instance);

    private static Product NewProduct(string name, int marketId) => new()
    {
        Id = Guid.NewGuid(), Name = name, MarketId = marketId, Unit = UnitType.Piece,
        Quantity = 10, SalePrice = 1000,
    };

    [Fact]
    public async Task Yangi_yozuvlar_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var payload = new SyncPushDto { Products = { NewProduct("Sement", 9) } };

        var result = await NewService(h).AcceptAsync(9, payload);

        Assert.Equal(1, result.Accepted);
        var stored = await h.Db.Products.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Sement", stored.Name);
        Assert.Equal(9, stored.MarketId);
    }

    /// <summary>
    /// ENG MUHIM chegara. Do'kon o'z yozuvlarida istalgan <c>MarketId</c>
    /// yubora oladi — noto'g'ri sozlangan yoki buzilgan nusxa qo'shni
    /// do'konning savdolarini o'zining deb yozib yuborishi mumkin edi.
    /// </summary>
    [Fact]
    public async Task Boshqa_dokon_raqami_majburan_almashtiriladi()
    {
        using var h = new TestHarness(marketId: null);
        // Do'kon 9, lekin yozuvda 7 turibdi.
        var payload = new SyncPushDto { Products = { NewProduct("Begona", 7) } };

        await NewService(h).AcceptAsync(9, payload);

        var stored = await h.Db.Products.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(9, stored.MarketId);
    }

    [Fact]
    public async Task Takroriy_yuborish_nusxa_yaratmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var product = NewProduct("Sement", 9);
        var service = NewService(h);

        await service.AcceptAsync(9, new SyncPushDto { Products = { product } });
        await service.AcceptAsync(9, new SyncPushDto { Products = { product } });

        Assert.Single(await h.Db.Products.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Mavjud_yozuv_yangilanadi()
    {
        using var h = new TestHarness(marketId: null);
        var product = NewProduct("Sement", 9);
        var service = NewService(h);
        await service.AcceptAsync(9, new SyncPushDto { Products = { product } });

        var changed = NewProduct("Sement M400", 9);
        changed.Id = product.Id;
        changed.SalePrice = 1250;
        await service.AcceptAsync(9, new SyncPushDto { Products = { changed } });

        var stored = await h.Db.Products.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Sement M400", stored.Name);
        Assert.Equal(1250, stored.SalePrice);
    }

    /// <summary>
    /// Sotuv o'z qatorlaridan OLDIN yozilishi kerak, aks holda tashqi kalit
    /// buziladi va butun to'plam rad etilardi.
    /// </summary>
    [Fact]
    public async Task Sotuv_va_qatorlari_birga_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var seller = new User
        {
            Id = Guid.NewGuid(), MarketId = 9, Username = "kassir",
            FullName = "Kassir", PasswordHash = "h", Role = Role.Seller,
        };
        h.Db.Users.Add(seller);
        await h.Db.SaveChangesAsync();

        var product = NewProduct("Sement", 9);
        var sale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = 9, SellerId = seller.Id,
            SaleNumber = 1, TotalAmount = 1000, PaidAmount = 1000, Status = SaleStatus.Paid,
        };
        // SaleItem da MarketId YO'Q — u marketga faqat sotuvi orqali tegishli.
        var item = new SaleItem
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, ProductId = product.Id,
            Quantity = 1, SalePrice = 1000, CostPrice = 800,
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, Amount = 1000,
            PaymentType = PaymentType.Cash, MarketId = 9,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto
        {
            Products = { product },
            Sales = { sale },
            SaleItems = { item },
            Payments = { payment },
        });

        Assert.Equal(4, result.Accepted);
        Assert.Single(await h.Db.Sales.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await h.Db.SaleItems.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await h.Db.Payments.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Bekor qilingan savdo ham yetib borishi SHART: aks holda bulutda u
    /// tirik bo'lib qolar va egasining telefonida hamon ko'rinardi.
    /// </summary>
    [Fact]
    public async Task Ochirilgan_savdo_ham_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var product = NewProduct("Sement", 9);
        product.IsDeleted = true;

        await NewService(h).AcceptAsync(9, new SyncPushDto { Products = { product } });

        var stored = await h.Db.Products.IgnoreQueryFilters().SingleAsync();
        Assert.True(stored.IsDeleted);
    }

    /// <summary>
    /// Navigatsiya xossalari halqa hosil qiladi (Sale → SaleItems → Sale).
    /// Ular tashlanmasa serializator cheksiz aylanardi.
    /// </summary>
    /// <summary>
    /// <c>SaleItem</c> da <c>MarketId</c> yo'q, ya'ni uni majburan
    /// almashtirib bo'lmaydi. Tekshiruvsiz do'kon QO'SHNI do'konning sotuviga
    /// qator qo'shib yuborishi mumkin edi va uning hisoboti jimgina
    /// buzilardi.
    /// </summary>
    [Fact]
    public async Task Begona_sotuvga_qator_qoshib_bolmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var seller = new User
        {
            Id = Guid.NewGuid(), MarketId = 7, Username = "qoshni",
            FullName = "Qo'shni", PasswordHash = "h", Role = Role.Seller,
        };
        var foreignSale = new Sale
        {
            Id = Guid.NewGuid(), MarketId = 7, SellerId = seller.Id,
            SaleNumber = 1, TotalAmount = 500,
        };
        h.Db.Users.Add(seller);
        h.Db.Sales.Add(foreignSale);
        await h.Db.SaveChangesAsync();

        // 9-do'kon 7-do'konning sotuviga qator qo'shmoqchi.
        var result = await NewService(h).AcceptAsync(9, new SyncPushDto
        {
            SaleItems = { new SaleItem
            {
                Id = Guid.NewGuid(), SaleId = foreignSale.Id,
                Quantity = 99, SalePrice = 1,
            } },
        });

        Assert.Equal(0, result.Accepted);
        Assert.Empty(await h.Db.SaleItems.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void Navigatsiya_xossalari_yuborilmaydi()
    {
        var sale = new Sale { Id = Guid.NewGuid(), MarketId = 9, SaleNumber = 1 };
        sale.SaleItems.Add(new SaleItem { Id = Guid.NewGuid(), SaleId = sale.Id });

        var json = JsonSerializer.Serialize(sale, EntityWireFormat.Options);
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        var keys = parsed.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Navigatsiyalar yo'q...
        Assert.DoesNotContain("saleItems", keys);
        Assert.DoesNotContain("payments", keys);
        Assert.DoesNotContain("seller", keys);
        Assert.DoesNotContain("customer", keys);
        Assert.DoesNotContain("market", keys);

        // ...lekin ularning TASHQI KALITLARI yuborilishi SHART, aks holda
        // bulutda bog'lanish yo'qolardi.
        Assert.Contains("sellerId", keys);
        Assert.Contains("saleNumber", keys);
        Assert.Contains("marketId", keys);
    }

    /// <summary>
    /// Vaqtlar simda UTC bo'lishi shart. API qolgan joyda ularni Toshkent
    /// mintaqasiga suradi va o'sha o'zgartirgich bu kanalga tegib ketsa,
    /// sotuv sanasi 5 soatga siljib, «bugungi tushum» noto'g'ri kun bo'yicha
    /// hisoblanardi.
    /// </summary>
    [Fact]
    public void Vaqtlar_utc_da_yuboriladi()
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            MarketId = 9,
            CreatedAt = new DateTime(2026, 8, 26, 7, 30, 0, DateTimeKind.Utc),
        };

        var json = JsonSerializer.Serialize(sale, EntityWireFormat.Options);
        Assert.Contains("2026-08-26T07:30:00.0000000Z", json);

        // Va qaytib o'qilganda ham UTC bo'lib qoladi.
        var back = JsonSerializer.Deserialize<Sale>(json, EntityWireFormat.Options)!;
        Assert.Equal(DateTimeKind.Utc, back.CreatedAt.Kind);
        Assert.Equal(sale.CreatedAt, back.CreatedAt);
    }

    // ── Ilgari umuman yuborilmagan jadvallar ──────────────────────────────
    // Bulutga faqat oltita jadval ketardi. Qarzlar ular orasida yo'q edi:
    // do'konda o'nlab mijozning millionlab so'm qarzi bo'lsa ham, egasining
    // telefonidagi «Qarzlar» ekrani NOL ko'rsatardi.

    /// <summary>Qarz o'z chekiga qo'shib yuborilsa — qabul qilinadi.</summary>
    [Fact]
    public async Task Qarz_chek_bilan_birga_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var customer = new Customer { Id = Guid.NewGuid(), MarketId = 9, Phone = "+998901112233" };
        var sale = new Sale { Id = Guid.NewGuid(), MarketId = 9, SellerId = Guid.NewGuid() };
        var debt = new Debt
        {
            Id = Guid.NewGuid(), MarketId = 9, SaleId = sale.Id, CustomerId = customer.Id,
            TotalDebt = 1_000_000, RemainingDebt = 400_000, Status = DebtStatus.Open,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto
        {
            Customers = { customer }, Sales = { sale }, Debts = { debt },
        });

        Assert.True(result.Accepted >= 3);
        var stored = await h.Db.Debts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(400_000, stored.RemainingDebt);
        Assert.Equal(9, stored.MarketId);
    }

    /// <summary>
    /// Cheki hali yetib bormagan qarz TASHLANMASLIGI kerak — u kechiktiriladi
    /// va keyingi urinishda o'tadi. Aks holda qarz abadiy yo'qolardi.
    /// </summary>
    [Fact]
    public async Task Cheki_yetmagan_qarz_kechiktiriladi()
    {
        using var h = new TestHarness(marketId: null);
        var debt = new Debt
        {
            Id = Guid.NewGuid(), MarketId = 9, SaleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(), TotalDebt = 500, RemainingDebt = 500,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto { Debts = { debt } });

        Assert.Empty(await h.Db.Debts.IgnoreQueryFilters().ToListAsync());
        Assert.True(result.Deferred.ContainsKey("Debt"), "qarz kechiktirilmadi");
    }

    /// <summary>
    /// Do'konda yaratilgan kassir bulutga o'tishi SHART. Ilgari xodimlar
    /// yuborilmasdi va uning birinchi cheki tashqi kalitni buzib, butun
    /// sinxronizatsiyani abadiy to'xtatardi.
    /// </summary>
    [Fact]
    public async Task Dokonda_yaratilgan_xodim_bulutga_otadi()
    {
        using var h = new TestHarness(marketId: null);
        var user = new User
        {
            Id = Guid.NewGuid(), MarketId = 9, Username = "kassir", FullName = "Kassir",
            PasswordHash = "x", Role = Role.Seller, IsActive = true,
        };

        await NewService(h).AcceptAsync(9, new SyncPushDto { Users = { user } });

        var stored = await h.Db.Users.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("kassir", stored.Username);
        Assert.Equal(9, stored.MarketId);
    }

    /// <summary>
    /// Kategoriya raqami bulutda BOSHQA do'konnikiga tegishli bo'lishi mumkin
    /// (u yerda ketma-ketlik hamma uchun umumiy). Shuning uchun havola
    /// uziladi — tovar begona kategoriyaga tushib qolgandan ko'ra
    /// kategoriyasiz qolgani yaxshi.
    /// </summary>
    [Fact]
    public async Task Tovar_begona_kategoriyaga_boglanmaydi()
    {
        using var h = new TestHarness(marketId: null);
        var product = NewProduct("Sement", 9);
        product.CategoryId = 7;                      // do'konning O'Z raqami

        await NewService(h).AcceptAsync(9, new SyncPushDto { Products = { product } });

        var stored = await h.Db.Products.IgnoreQueryFilters().SingleAsync();
        Assert.Null(stored.CategoryId);
    }

    // ── Umumlashtirilgan kechiktirish ─────────────────────────────────────
    // Ilgari kechiktirish FAQAT sotuv otasi uchun ishlardi. Boshqa har qanday
    // tashqi kalit — qaytarish qatorining otasi, xarid qatorining tovari —
    // paket chegarasi tufayli hali yetib bormagan bo'lsa, BUTUN to'plam rad
    // etilardi va do'kon aynan o'sha paketni qayta yuborib, hech qachon o'ta
    // olmasdi.

    /// <summary>Qaytarish o'z cheki bilan birga o'tadi.</summary>
    [Fact]
    public async Task Qaytarish_chek_bilan_birga_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var sale = new Sale { Id = Guid.NewGuid(), MarketId = 9, SellerId = Guid.NewGuid() };
        var ret = new SaleReturn
        {
            Id = Guid.NewGuid(), MarketId = 9, SaleId = sale.Id, Number = 1,
            TotalAmount = 5000,
        };
        var item = new SaleReturnItem
        {
            Id = Guid.NewGuid(), SaleReturnId = ret.Id, Quantity = 1, UnitPrice = 5000,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto
        {
            Sales = { sale }, SaleReturns = { ret }, SaleReturnItems = { item },
        });

        Assert.True(result.Accepted >= 3);
        Assert.Single(await h.Db.SaleReturns.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await h.Db.SaleReturnItems.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Otasi yetib bormagan qaytarish qatori TASHLANMAYDI — kechiktiriladi.
    /// </summary>
    [Fact]
    public async Task Otasi_yetmagan_qaytarish_qatori_kechiktiriladi()
    {
        using var h = new TestHarness(marketId: null);
        var item = new SaleReturnItem
        {
            Id = Guid.NewGuid(), SaleReturnId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto { SaleReturnItems = { item } });

        Assert.Empty(await h.Db.SaleReturnItems.IgnoreQueryFilters().ToListAsync());
        Assert.True(result.Deferred.ContainsKey("SaleReturnItem"));
    }

    /// <summary>
    /// Tovari hali yetib bormagan xarid qatori ham kechiktiriladi — aks holda
    /// tashqi kalit butun to'plamni rad ettirardi.
    /// </summary>
    [Fact]
    public async Task Tovari_yetmagan_xarid_kechiktiriladi()
    {
        using var h = new TestHarness(marketId: null);
        var zakup = new Zakup
        {
            Id = Guid.NewGuid(), MarketId = 9, ProductId = Guid.NewGuid(),
            CreatedByAdminId = Guid.NewGuid(), Quantity = 10, CostPrice = 1000,
        };

        var result = await NewService(h).AcceptAsync(9, new SyncPushDto { Zakups = { zakup } });

        Assert.Empty(await h.Db.Zakups.IgnoreQueryFilters().ToListAsync());
        Assert.True(result.Deferred.ContainsKey("Zakup"));
    }

    /// <summary>Xarid o'z tovari bilan birga kelsa — o'tadi.</summary>
    [Fact]
    public async Task Xarid_tovari_bilan_birga_qabul_qilinadi()
    {
        using var h = new TestHarness(marketId: null);
        var product = NewProduct("Sement", 9);
        var zakup = new Zakup
        {
            Id = Guid.NewGuid(), MarketId = 9, ProductId = product.Id,
            CreatedByAdminId = Guid.NewGuid(), Quantity = 10, CostPrice = 1000,
        };

        await NewService(h).AcceptAsync(9, new SyncPushDto
        {
            Products = { product }, Zakups = { zakup },
        });

        Assert.Single(await h.Db.Zakups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>
    /// Kassa harakatining majburiy tashqi kaliti yo'q — u tekshiruvsiz
    /// o'tishi kerak, aks holda kassa qoldig'i bulutda hech qachon
    /// ko'rinmasdi.
    /// </summary>
    [Fact]
    public async Task Kassa_harakati_toqnashuvsiz_otadi()
    {
        using var h = new TestHarness(marketId: null);
        var move = new CashMovement
        {
            Id = Guid.NewGuid(), MarketId = 9, Amount = 50_000,
            Type = CashMovementType.Sale,
        };

        await NewService(h).AcceptAsync(9, new SyncPushDto { CashMovements = { move } });

        var stored = await h.Db.CashMovements.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(50_000, stored.Amount);
    }
}
