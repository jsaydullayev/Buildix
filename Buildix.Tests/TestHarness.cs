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

        Db = new AppDbContext(options, Market);
        UnitOfWork = new UnitOfWork(Db, NullLogger<UnitOfWork>.Instance);
    }

    // Real credit applier over the InMemory context. The substituted
    // ICustomerService returns 0 available credit by default, so credit
    // application is a no-op in tests unless a test arranges otherwise.
    public SaleCreditApplier CreditApplier => new(Db, Market, Customers, NullLogger<SaleCreditApplier>.Instance);

    public SaleQueryService NewSaleQueryService() =>
        new(UnitOfWork, Db, Market, NullLogger<SaleQueryService>.Instance);

    public SaleService NewSaleService() =>
        new(UnitOfWork, Audit, Db, NullLogger<SaleService>.Instance, Market, CreditApplier, NewSaleQueryService(), Settings);

    public SaleItemService NewSaleItemService() =>
        new(UnitOfWork, Db, Market, NullLogger<SaleItemService>.Instance, CreditApplier, Settings, Audit);

    public SaleReversalService NewSaleReversalService() =>
        new(UnitOfWork, Db, Market, Audit, NullLogger<SaleReversalService>.Instance);

    public SalePaymentService NewSalePaymentService() =>
        new(UnitOfWork, Db, Market, Audit, NullLogger<SalePaymentService>.Instance, Settings);

    public IStockLedger StockLedger => new StockLedger(Db);

    public ProductService NewProductService() =>
        new(UnitOfWork, Db, Market, Audit, StockLedger);

    public ProductQueryService NewProductQueryService() =>
        new(Db, Market);

    public ProductImageService NewProductImageService() =>
        new(UnitOfWork, Market, ImageStorage, Audit);

    public DebtService NewDebtService() =>
        new(Db, UnitOfWork, Market, Audit, NullLogger<DebtService>.Instance);

    public void Dispose() => Db.Dispose();
}
