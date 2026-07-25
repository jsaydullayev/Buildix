using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// Telegram bot kunlik xulosasi — sotuv/foyda/kassa-qarz/ombor raqamlari.
/// Servis marketId'ni ATAYLAB parametr sifatida oladi (fon vazifasida HTTP
/// konteksti yo'q → tenant filtri ishlamaydi), shuning uchun bu yerda market
/// izolyatsiyasi ham tekshiriladi.
/// </summary>
public class TelegramDailySummaryTests
{
    private const int Market = 1;
    private const int OtherMarket = 2;

    private static Sale AddSale(TestHarness h, int marketId, decimal total, DateTime createdAt,
        SaleStatus status = SaleStatus.Paid)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            SaleNumber = Random.Shared.Next(1, 100_000),
            Status = status,
            TotalAmount = total,
            CreatedAt = createdAt,
        };
        h.Db.Sales.Add(sale);
        return sale;
    }

    private static void AddItem(TestHarness h, Sale sale, string name, decimal qty, decimal sale_, decimal cost)
    {
        // External items carry their own name/cost — no Product row needed, and
        // this is the path the summary uses for the "top products" grouping.
        h.Db.SaleItems.Add(new SaleItem
        {
            Id = Guid.NewGuid(),
            SaleId = sale.Id,
            IsExternal = true,
            ExternalProductName = name,
            ExternalCostPrice = cost,
            Quantity = qty,
            SalePrice = sale_,
        });
    }

    private static void AddPayment(TestHarness h, Sale sale, PaymentType type, decimal amount, DateTime at)
    {
        h.Db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            MarketId = sale.MarketId,
            SaleId = sale.Id,
            PaymentType = type,
            Amount = amount,
            CreatedAt = at,
        });
    }

    private static async Task SeedMarketAsync(TestHarness h, int id, string name)
    {
        h.Db.Markets.Add(new Market { Id = id, Name = name });
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Summary_reports_sales_profit_cash_debts_and_stock()
    {
        using var h = new TestHarness(Market);
        await SeedMarketAsync(h, Market, "Demo do'kon");

        var today = h.Clock.TodayLocal;
        var noonUtc = h.Clock.LocalDayToUtcRange(today).UtcStart.AddHours(12);

        // Ikki bugungi chek: 100 000 (naqd) + 60 000 (terminal).
        var s1 = AddSale(h, Market, 100_000m, noonUtc);
        AddItem(h, s1, "Sement M500", 2m, 50_000m, 30_000m); // foyda 40 000
        AddPayment(h, s1, PaymentType.Cash, 100_000m, noonUtc);

        var s2 = AddSale(h, Market, 60_000m, noonUtc);
        AddItem(h, s2, "Armatura", 1m, 60_000m, 45_000m); // foyda 15 000
        AddPayment(h, s2, PaymentType.Terminal, 60_000m, noonUtc);

        // Kechagi chek — bugungi hisobga KIRMASLIGI kerak.
        var old = AddSale(h, Market, 999_000m, noonUtc.AddDays(-1));
        AddItem(h, old, "Eski tovar", 1m, 999_000m, 0m);

        // Draft — hech qachon tushum emas.
        AddSale(h, Market, 777_000m, noonUtc, SaleStatus.Draft);

        // Boshqa marketning chegi — SIZIB CHIQMASLIGI kerak.
        var foreign = AddSale(h, OtherMarket, 500_000m, noonUtc);
        AddItem(h, foreign, "Begona", 1m, 500_000m, 0m);

        // Kassa qoldig'i + ochiq (muddati o'tgan) qarz.
        h.Db.CashRegisters.Add(new CashRegister { Id = Guid.NewGuid(), MarketId = Market, CurrentBalance = 250_000m });
        h.Db.Debts.Add(new Debt
        {
            Id = Guid.NewGuid(),
            MarketId = Market,
            CustomerId = Guid.NewGuid(),
            SaleId = s1.Id,
            TotalDebt = 80_000m,
            RemainingDebt = 80_000m,
            Status = DebtStatus.Open,
            DueDate = h.Clock.UtcNow.AddDays(-3), // просрочено
            CreatedAt = noonUtc.AddDays(-5),
        });

        // Ombor: bittasi tugagan, bittasi kam qolgan, bittasi yetarli.
        h.Db.Products.AddRange(
            new Product { Id = Guid.NewGuid(), MarketId = Market, Name = "Bo'yoq", Quantity = 0m, MinThreshold = 5m },
            new Product { Id = Guid.NewGuid(), MarketId = Market, Name = "Mix", Quantity = 3m, MinThreshold = 10m },
            new Product { Id = Guid.NewGuid(), MarketId = Market, Name = "G'isht", Quantity = 900m, MinThreshold = 100m });
        await h.Db.SaveChangesAsync();

        var text = await h.NewTelegramDailySummaryService().BuildAsync(Market, today);

        Assert.NotNull(text);
        Assert.Contains("Demo do'kon", text);

        // Sotuv: 2 chek, 160 000 tushum (kechagi/draft/begona kirmagan).
        Assert.Contains("Чеков: 2", text);
        Assert.Contains("160 000", text);
        Assert.DoesNotContain("999 000", text);
        Assert.DoesNotContain("777 000", text);
        Assert.DoesNotContain("500 000", text);
        Assert.DoesNotContain("Begona", text);

        // To'lov taqsimoti: naqd 100 000, karta 60 000.
        Assert.Contains("Наличные: 100 000", text);
        Assert.Contains("Карта: 60 000", text);

        // Foyda 40 000 + 15 000 = 55 000.
        Assert.Contains("55 000", text);

        // Kassa + muddati o'tgan qarz.
        Assert.Contains("250 000", text);
        Assert.Contains("Просрочено", text);
        Assert.Contains("80 000", text);

        // Ombor signali: tugagan 1, kam qolgan 2 (yetarlisi ro'yxatда yo'q).
        Assert.Contains("Закончилось: 1", text);
        Assert.Contains("Заканчивается: 2", text);
        Assert.Contains("Bo'yoq", text);
        Assert.DoesNotContain("G'isht", text);

        // Top tovar — eng katta summali birinchi.
        Assert.Contains("1. Sement M500", text);
    }

    [Fact]
    public async Task Summary_handles_a_day_with_no_sales()
    {
        using var h = new TestHarness(Market);
        await SeedMarketAsync(h, Market, "Bo'sh do'kon");

        var text = await h.NewTelegramDailySummaryService().BuildAsync(Market, h.Clock.TodayLocal);

        Assert.NotNull(text);
        Assert.Contains("Продаж за день нет", text);
        // Bo'sh kunda ham kassa/ombor bloklari bo'ladi (0 bilan).
        Assert.Contains("Касса и долги", text);
        Assert.Contains("Все товары в достатке", text);
    }

    [Fact]
    public async Task Summary_returns_null_for_unknown_market()
    {
        using var h = new TestHarness(Market);

        var text = await h.NewTelegramDailySummaryService().BuildAsync(999, h.Clock.TodayLocal);

        Assert.Null(text);
    }

    [Fact]
    public async Task Summary_escapes_html_in_names()
    {
        using var h = new TestHarness(Market);
        // Telegram parse_mode=HTML — eskeyplanmasa butun xabar yuborilmaydi.
        await SeedMarketAsync(h, Market, "Do'kon <B&M>");
        await h.Db.SaveChangesAsync();

        var text = await h.NewTelegramDailySummaryService().BuildAsync(Market, h.Clock.TodayLocal);

        Assert.NotNull(text);
        Assert.Contains("Do'kon &lt;B&amp;M&gt;", text);
    }
}
