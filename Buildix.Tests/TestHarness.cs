using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Buildix.Infrastructure.Data;
using Buildix.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Buildix.Tests;

/// <summary>
/// Settable tenant provider. The AppDbContext global query filters read this,
/// so a test drives multi-tenancy by flipping <see cref="MarketId"/>.
/// Null models a no-market context (SuperAdmin / startup) — filters disabled.
/// </summary>
public sealed class FakeCurrentMarketService : ICurrentMarketService
{
    public int? MarketId { get; set; }

    public int GetCurrentMarketId() =>
        MarketId ?? throw new UnauthorizedAccessException("No market in test context.");

    public int? TryGetCurrentMarketId() => MarketId;
}

/// <summary>
/// Bulut bilan aloqa holati — sinovda qo'lda qo'yiladi.
///
/// <para>Sukut bo'yicha «bog'langan va yangi»: mavjud sinovlarning
/// hech biri qarz darvozasiga urilmasligi kerak, chunki qoida
/// (<c>DebtRequiresCloud</c>) ham sukut bo'yicha o'chiq.</para>
/// </summary>
public sealed class FakeSyncFreshness : ISyncFreshnessService
{
    public bool IsPaired { get; set; } = true;
    public bool IsFresh { get; set; } = true;
    public string? Error { get; set; }

    public Task<SyncFreshnessDto> GetAsync(int marketId, CancellationToken ct = default) =>
        Task.FromResult(new SyncFreshnessDto(IsPaired, IsFresh, null, null, null, Error));
}

/// <summary>
/// So'rov kelgan kassaning belgisi — sinovda qo'lda qo'yiladi.
/// Sukut bo'yicha <c>null</c>: brauzerdan kirilgan holat.
/// </summary>
public sealed class FakeCurrentRegisterService : ICurrentRegisterService
{
    public string? Code { get; set; }

    public string? GetRegisterCode() => Code;
}

/// <summary>
/// Qo'lda suriladigan soat. Boshlang'ich nuqta o'tmishdagi aniq sana:
/// testdagi vaqtlar tizim soatiga bog'liq bo'lmasin.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Soatni oldinga suradi.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

/// <summary>
/// One isolated EF-InMemory database per test, wired with the real
/// AppDbContext + UnitOfWork so query filters, SUM-based totals and status
/// transitions run for real. Only the peripheral collaborators (audit log,
/// customer-credit) are substituted. The FOR UPDATE raw-SQL locks are guarded
/// by a provider check in the services and are skipped on InMemory.
/// </summary>
public sealed class TestHarness : IDisposable
{
    public AppDbContext Db { get; }
    public FakeCurrentMarketService Market { get; } = new();
    public IUnitOfWork UnitOfWork { get; }
    public IAuditLogService Audit { get; } = Substitute.For<IAuditLogService>();
    public ICustomerService Customers { get; } = Substitute.For<ICustomerService>();
    public IProductImageStorage ImageStorage { get; } = Substitute.For<IProductImageStorage>();
    public IMarketSettingsService Settings { get; } = Substitute.For<IMarketSettingsService>();

    public TestHarness(int? marketId = 1)
    {
        Market.MarketId = marketId;

        // Enforcement rules OFF by default so the happy-path money tests behave
        // as they did before MarketSettings enforcement existed. A test that
        // wants to exercise a rule (debt-limit, regulars-only, below-cost,
        // shift-open) re-stubs Settings.GetOrCreateAsync(...) with its own row.
        Settings.GetOrCreateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MarketSettings
            {
                SalesOnlyWhenShiftOpen = false,
                DebtOnlyForRegulars = false,
                BlockSaleBelowCost = false,
                CashWithdrawalNeedsApproval = false,
                DefaultDebtLimit = 0m,
            });

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"buildix-test-{Guid.NewGuid()}")
            // InMemory has no real transactions; the UnitOfWork opens one, so
            // silence the otherwise-thrown "transaction ignored" warning.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Db = new AppDbContext(options, Market, DbClock);
        UnitOfWork = new UnitOfWork(Db, NullLogger<UnitOfWork>.Instance);
    }

    /// <summary>
    /// Bazaga yoziladigan <c>UpdatedAt</c> ni boshqaradigan soat. Yuqoridagi
    /// <see cref="Clock"/> dan boshqa narsa: u — Toshkent biznes soati (smena,
    /// kechikish), bu esa yozuv vaqtini belgilaydi.
    ///
    /// <para>Boshqariladigan qilingan, chunki tizim soatiga tayanib bo'lmaydi:
    /// uning aniqligi Windows'da ~15 ms va ketma-ket ikki saqlash bir xil vaqt
    /// olishi mumkin.</para>
    /// </summary>
    public TestClock DbClock { get; } = new();

    // Real credit applier over the InMemory context. The substituted
    // ICustomerService returns 0 available credit by default, so credit
    // application is a no-op in tests unless a test arranges otherwise.
    public SaleCreditApplier CreditApplier => new(Db, Market, Customers, NullLogger<SaleCreditApplier>.Instance);

    public SaleQueryService NewSaleQueryService() =>
        new(UnitOfWork, Db, Market, NullLogger<SaleQueryService>.Instance);

    /// <summary>
    /// So'rov qaysi kassadan kelgani. Sinovda qo'lda qo'yiladi — HTTP
    /// sarlavhasi yo'q.
    /// </summary>
    public FakeCurrentRegisterService Register { get; } = new();

    /// <summary>Bulut bilan aloqa holati (qarz darvozasi uchun).</summary>
    public FakeSyncFreshness Freshness { get; } = new();

    public SaleService NewSaleService() =>
        new(UnitOfWork, Audit, Db, NullLogger<SaleService>.Instance, Market, CreditApplier,
            NewSaleQueryService(), Settings, StockLedger, ExternalPayouts, Register, Freshness);

    public SaleItemService NewSaleItemService() =>
        new(UnitOfWork, Db, Market, NullLogger<SaleItemService>.Instance, CreditApplier, Settings, Audit);

    public SaleReversalService NewSaleReversalService() =>
        new(UnitOfWork, Db, Market, Audit, NullLogger<SaleReversalService>.Instance,
            StockLedger, CashLedger, ExternalPayouts);

    public SaleReturnService NewSaleReturnService() =>
        new(UnitOfWork, Db, Market, Audit, StockLedger, CashLedger);

    public SalePaymentService NewSalePaymentService() =>
        new(UnitOfWork, Db, Market, Audit, NullLogger<SalePaymentService>.Instance, Settings,
            StockLedger, CashLedger, CreditApplier, Freshness, ExternalPayouts);

    public ProductLabelService NewProductLabelService() =>
        new(Db, UnitOfWork, Market);

    public IStockLedger StockLedger => new StockLedger(Db);
    public ICashLedger CashLedger => new CashLedger(Db);
    public IExternalPayoutLedger ExternalPayouts => new ExternalPayoutLedger(Db, CashLedger);

    // Deterministic UTC+5 clock — no OS tz-db dependency, so ToLocal/TodayLocal
    // behave identically on any CI host (Смены attendance/lateness rely on this).
    public ITashkentClock Clock { get; } =
        new TashkentClock(TimeZoneInfo.CreateCustomTimeZone("TST", TimeSpan.FromHours(5), "Tashkent", "Tashkent"));

    public ShiftService NewShiftService() =>
        new(UnitOfWork, Db, Market, Audit, Settings, Substitute.For<ITelegramNotifier>(),
            Clock, CashLedger, Substitute.For<INotificationService>());

    public CashRegisterService NewCashRegisterService() =>
        new(UnitOfWork, NullLogger<CashRegisterService>.Instance, Db, Market, Clock, Audit,
            Settings, Substitute.For<ITelegramNotifier>(), CashLedger);

    public ProductService NewProductService() =>
        new(UnitOfWork, Db, Market, Audit, StockLedger);

    public ProductQueryService NewProductQueryService() =>
        new(Db, Market, Clock);

    public ProductImageService NewProductImageService() =>
        new(UnitOfWork, Market, ImageStorage, Audit);

    public DebtService NewDebtService() =>
        new(Db, UnitOfWork, Market, Audit, NullLogger<DebtService>.Instance, CashLedger,
            Settings, Freshness);

    // Telegram bot day summary. Deliberately takes marketId per call (it also
    // runs in a background job with no tenant context), so it does not use Market.
    public TelegramDailySummaryService NewTelegramDailySummaryService() =>
        new(Db, Clock);

    public void Dispose() => Db.Dispose();
}
