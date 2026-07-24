using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentMarketService? _currentMarket;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentMarketService? currentMarket = null)
        : base(options)
    {
        _currentMarket = currentMarket;
    }

    // Defense-in-depth tenant scope for the global query filters in
    // OnModelCreating. Returns null when there is NO market context —
    // SuperAdmin requests (their JWT carries no MarketId), the anonymous login
    // path, startup MigrateAsync, and the design-time factory (which builds the
    // context with no ICurrentMarketService). In that case the tenant filter is
    // a no-op and returns every row, so cross-market admin, login and
    // migrations keep working. For a market-scoped request it returns that
    // market's id and every tenant-owned entity's read is auto-filtered — a
    // backstop so a single forgotten `.Where(x => x.MarketId == marketId)`
    // in a service can never leak another market's data.
    //
    // Read per-query on the executing context (EF Core supports referencing the
    // current DbContext instance inside a query filter), so the cached model
    // does NOT pin one request's market to all requests.
    private int? TenantMarketId => _currentMarket?.TryGetCurrentMarketId();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtAuditLog> DebtAuditLogs => Set<DebtAuditLog>();
    public DbSet<Zakup> Zakups => Set<Zakup>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ZakupReceipt> ZakupReceipts => Set<ZakupReceipt>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashWithdrawal> CashWithdrawals => Set<CashWithdrawal>();
    public DbSet<RegistrationRequest> RegistrationRequests => Set<RegistrationRequest>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<MarketSettings> MarketSettings => Set<MarketSettings>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure RevokedToken
        modelBuilder.Entity<RevokedToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Jti).IsRequired().HasMaxLength(200);
            b.HasIndex(x => x.Jti).IsUnique();
            b.HasIndex(x => x.ExpiresAtUtc);
        });

        // Configure LoginAttempt — brute-force lockout state (M1).
        // The Username uniqueness is enforced at the DB layer so two
        // concurrent failure-record writes for the same user can't create
        // duplicate rows; DbLoginAttemptTracker upserts against this key.
        modelBuilder.Entity<LoginAttempt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.FailureCount).IsRequired();
            b.Property(x => x.FirstFailureUtc).IsRequired();
            b.Property(x => x.UpdatedAtUtc).IsRequired();
            // Sweep query: "rows whose LockedUntilUtc is past AND
            // FirstFailureUtc + Window is past" — UpdatedAtUtc as a single
            // anchor index covers the cleanup pass at startup.
            b.HasIndex(x => x.UpdatedAtUtc);
        });

        // Configure Market
        modelBuilder.Entity<Market>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Subdomain).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => x.Subdomain).IsUnique();
            b.HasIndex(x => x.Name).IsUnique();  // Market nomi unikal bo'lishi kerak

            // Block state — TenantResolutionMiddleware queries (Id, IsBlocked) on
            // every authenticated request, so cover both columns with a partial
            // index. The partial index keeps the on-disk size tiny because the
            // common case is `IsBlocked = false`.
            b.Property(x => x.IsBlocked).HasDefaultValue(false);
            b.Property(x => x.BlockedReason).HasMaxLength(500);
            b.HasIndex(x => new { x.Id, x.IsBlocked })
                .HasFilter("\"IsBlocked\" = TRUE")
                .HasDatabaseName("IX_Markets_Blocked");

            // Owner relationship
            b.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId);
            b.HasIndex(x => x.OwnerId);
        });

        // Configure LoginHistory — user-facing sign-in history (Account screen).
        modelBuilder.Entity<LoginHistory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.DeviceInfo).HasMaxLength(120);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            // "recent logins for this user" — covered by (UserId, CreatedAt desc).
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        // Configure MarketSettings — 1:1 with Market, sharing its int key.
        // The row is created lazily by the settings service; every business
        // toggle/limit the Settings screen exposes is enforced from here.
        modelBuilder.Entity<MarketSettings>(b =>
        {
            b.HasKey(x => x.MarketId);
            b.Property(x => x.MarketId).ValueGeneratedNever();
            b.Property(x => x.Phone).HasMaxLength(30);
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.WorkingHours).HasMaxLength(100);
            b.Property(x => x.ReceiptHeader).HasMaxLength(300);
            b.Property(x => x.ReceiptFooter).HasMaxLength(300);
            b.Property(x => x.OwnerTelegram).HasMaxLength(100);
            b.Property(x => x.DefaultDebtLimit).HasPrecision(18, 2);
            b.Property(x => x.AllowedCashDiscrepancy).HasPrecision(18, 2);
            b.Property(x => x.DefaultMarkupPct).HasPrecision(9, 2);
            b.Property(x => x.DefaultLanguage).HasDefaultValue(Language.Russian);
            b.HasOne(x => x.Market).WithOne().HasForeignKey<MarketSettings>(x => x.MarketId);
        });

        // Idempotency keys for money-moving endpoints. No tenant query filter —
        // it's an infra table always accessed with an explicit MarketId scope.
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Scope).HasMaxLength(64).IsRequired();
            b.Property(x => x.Key).HasMaxLength(200).IsRequired();
            b.Property(x => x.RequestHash).HasMaxLength(64);
            b.Property(x => x.ResponseBody).HasColumnType("text");
            // Unique per (market, scope, key). The INSERT-pending race relies on
            // this: the loser of a concurrent duplicate hits the unique violation
            // (23505) and is routed to replay / in-progress.
            b.HasIndex(x => new { x.MarketId, x.Scope, x.Key }).IsUnique();
            // Retention pruning helper.
            b.HasIndex(x => x.CreatedAt);
        });

        // Configure Customer
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Phone).IsRequired().HasMaxLength(20);
            b.Property(x => x.FullName).HasMaxLength(200);
            b.Property(x => x.Comment).HasMaxLength(500);
            b.Property(x => x.DebtLimit).HasPrecision(18, 2);
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));

            // Multi-tenancy
            b.HasOne(x => x.Market).WithMany(m => m.Customers).HasForeignKey(x => x.MarketId);
            b.HasIndex(x => x.MarketId);
            // Phone is unique per market — different markets can share customer phones.
            b.HasIndex(x => new { x.MarketId, x.Phone }).IsUnique();
        });

        // Configure User
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            b.Property(x => x.Username).IsRequired().HasMaxLength(100);
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(20);
            b.Property(x => x.Telegram).HasMaxLength(100);
            b.Property(x => x.Language).HasDefaultValue(Language.Uzbek).IsRequired();
            // ProfileImage stores base64 encoded image data - use TEXT type for unlimited size
            b.Property(x => x.ProfileImage).HasColumnType("text");
            // Owner RBAC — explicit permission set as a PostgreSQL text[] array.
            // Npgsql maps List<string> to text[] natively; default is an empty array.
            b.Property(x => x.Permissions)
                .HasColumnType("text[]")
                .HasDefaultValueSql("'{}'::text[]");
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));

            // Multi-tenancy
            b.HasOne(x => x.Market).WithMany(m => m.Users).HasForeignKey(x => x.MarketId);
            b.HasIndex(x => x.MarketId);
            // Username scope:
            //   - Tenant users (MarketId IS NOT NULL): unique per market.
            //   - Cross-tenant users (MarketId IS NULL, e.g. SuperAdmin): globally unique.
            b.HasIndex(x => new { x.MarketId, x.Username })
                .IsUnique()
                .HasFilter("\"MarketId\" IS NOT NULL")
                .HasDatabaseName("IX_Users_MarketId_Username_Unique");
            b.HasIndex(x => x.Username)
                .IsUnique()
                .HasFilter("\"MarketId\" IS NULL")
                .HasDatabaseName("IX_Users_Username_GlobalUnique");
        });

        // Configure Product
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Sku).HasMaxLength(50);
            b.Property(x => x.CostPrice).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.SalePrice).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.MinSalePrice).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Quantity).HasPrecision(18, 3).IsRequired();
            b.Property(x => x.MinThreshold).HasPrecision(18, 3).IsRequired();

            // Optimistic concurrency via PostgreSQL system column xmin.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // ✅ Category relationship (optional)
            b.HasOne(x => x.Category).WithMany(c => c.Products).HasForeignKey(x => x.CategoryId);

            b.HasOne(x => x.CreatedBySeller).WithMany(p => p.TemporaryProducts).HasForeignKey(x => x.CreatedBySellerId);
            b.HasOne(x => x.Market).WithMany(m => m.Products).HasForeignKey(x => x.MarketId);
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));
            b.HasIndex(x => x.MarketId);
            b.HasIndex(x => x.CategoryId);  // ✅ Index for faster filtering
            // Product name is unique per market. Partial index excludes
            // soft-deleted rows so re-adding a product after delete works.
            b.HasIndex(x => new { x.MarketId, x.Name })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false")
                .HasDatabaseName("IX_Products_MarketId_Name_Active");

            // P5 — low-stock scan: faqat Quantity <= MinThreshold bo'lgan qatorlar.
            // GetLowStockProductsAsync uchun — butun market scan o'rniga kichik
            // partial index ishlatiladi (odatda 1-5% tovarlar low-stock bo'ladi).
            // Eslatma: pagination uchun alohida index kerak emas — mavjud
            // IX_Products_MarketId_Name_Active (MarketId, Name) composite unique
            // index ORDER BY Name so'rovlari uchun ham ishlatiladi.
            b.HasIndex(x => x.MarketId)
                .HasFilter("\"Quantity\" <= \"MinThreshold\" AND \"IsDeleted\" = false")
                .HasDatabaseName("IX_Products_LowStock");
        });

        // ✅ Configure ProductCategory
        modelBuilder.Entity<ProductCategory>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Icon).HasMaxLength(32);  // Single emoji glyph (ZWJ sequences fit in 32)
            b.Property(x => x.MarketId).IsRequired();  // ✅ NOT NULL - required
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.UpdatedAt).IsRequired();
            b.Property(x => x.IsDeleted).IsRequired();
            b.Property(x => x.DeletedAt).IsRequired(false);

            // Multi-tenancy - Market foreign key (no navigation property needed)
            b.HasIndex(x => x.MarketId);
            b.HasIndex(x => x.Name);  // ✅ Index for searching by name
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));

            // ✅ Navigation property
            b.HasMany(x => x.Products).WithOne(p => p.Category).HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);  // ✅ Category o'chirilsa, Product.CategoryId = NULL
        });

        // Configure Sale
        modelBuilder.Entity<Sale>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.PaidAmount).HasPrecision(18, 2);
            b.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            // Per-market sequential receipt number (ЧЕК №). Non-unique index —
            // ordering/lookup helper. Allocation is collision-free: SaleService
            // takes a per-market advisory lock (MarketSequenceLock) before the
            // max+1 read, so concurrent creates can't reuse a number.
            b.HasIndex(x => new { x.MarketId, x.SaleNumber });

            // The drawer session this sale was rung up in (receipt "Смена №N").
            // Optional + Restrict: shift history must never cascade-delete sales.
            b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ShiftId);

            // Xmin concurrency token disabled for Sale — too many modifications
            // in a single transaction (items add/remove, status change, debt update)
            // cause false conflicts. Use database-level constraints instead (FK, CHECK).
            // If needed, implement optimistic locking at the application layer with
            // a dedicated RowVersion column (int/long) instead of PostgreSQL xmin.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate();
                // .IsConcurrencyToken(); // DISABLED — see above

            // IMPORTANT: Seller/User o'chirilsa, Sale tarixi o'CHMASIN kerak
            b.HasOne(x => x.Seller).WithMany(p => p.Sales).HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.Restrict);

            // IMPORTANT: Customer o'chirilsa, Sale tarixi o'CHMASIN kerak
            b.HasOne(x => x.Customer).WithMany(p => p.Sales).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Market).WithMany(m => m.Sales).HasForeignKey(x => x.MarketId);

            b.HasOne(x => x.Debt).WithOne(x => x.Sale).HasForeignKey<Debt>(x => x.SaleId);

            // Indexes for performance
            b.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("IX_Sale_Status_CreatedAt");
            b.HasIndex(x => x.CustomerId)
                .HasDatabaseName("IX_Sale_CustomerId");
            b.HasIndex(x => new { x.SellerId, x.Status })
                .HasDatabaseName("IX_Sale_Seller_Status");
            // P1 — the hottest sales-list query is per-market, ordered by
            // CreatedAt DESC (POS history, paged + filtered). The single
            // (MarketId) index narrowed rows but PG still had to sort. This
            // composite lets the planner do an index-only range scan in
            // reverse date order without a separate sort step.
            b.HasIndex(x => new { x.MarketId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_Sale_Market_CreatedAt_Desc");

            // Soft delete filter
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));
        });

        // Configure SaleItem
        modelBuilder.Entity<SaleItem>(b =>
        {
            b.HasKey(x => x.Id);

            // ✅ IsExternal - mandatory, default false
            b.Property(x => x.IsExternal)
                .IsRequired()
                .HasDefaultValue(false);

            // ✅ ProductId - nullable bo'lishi mumkin
            b.Property(x => x.ProductId)
                .IsRequired(false);  // Nullable FK

            // ✅ ExternalProductName - nullable bo'lishi mumkin
            b.Property(x => x.ExternalProductName)
                .HasMaxLength(200)
                .IsRequired(false);

            // ✅ ExternalCostPrice - default 0
            b.Property(x => x.ExternalCostPrice)
                .HasPrecision(18, 2)
                .HasDefaultValue(0m);

            b.Property(x => x.Quantity).HasPrecision(18, 3).IsRequired();
            b.Property(x => x.CostPrice).HasPrecision(18, 2);
            b.Property(x => x.SalePrice).HasPrecision(18, 2);
            b.Property(x => x.Comment).HasMaxLength(500);

            b.HasOne(x => x.Sale).WithMany(p => p.SaleItems).HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Cascade); // Sale o'chirilsa, SaleItemlar ham o'chadi

            // ✅ Product relationship - nullable FK bo'lishi mumkin
            // IMPORTANT: Product o'chirilganda SaleItemlar o'CHMASIN kerak (tarix saqlash uchun)
            b.HasOne(x => x.Product).WithMany(p => p.SaleItems).HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict); // Product o'chirilsa, SaleItemlar qoladi

            // Index for performance
            b.HasIndex(x => new { x.SaleId, x.ProductId })
                .HasDatabaseName("IX_SaleItem_Sale_Product");
        });

        // Configure Payment
        modelBuilder.Entity<Payment>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 2);

            b.HasOne(x => x.Sale).WithMany(p => p.Payments).HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Cascade); // Sale o'chirilsa, Paymentlar ham o'chadi

            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);

            // Who actually took the money (debt collected by another cashier).
            // Optional + Restrict: removing a user must never cascade-delete
            // financial records, and NULL keeps the "sale's seller" fallback.
            b.HasOne(x => x.CollectedByUser).WithMany().HasForeignKey(x => x.CollectedByUserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Index for performance
            b.HasIndex(x => x.SaleId)
                .HasDatabaseName("IX_Payment_SaleId");
            b.HasIndex(x => x.MarketId);
            // Shift reconciliation filters payments by collector + time window.
            b.HasIndex(x => x.CollectedByUserId);
        });

        // Configure Debt
        modelBuilder.Entity<Debt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.TotalDebt).HasPrecision(18, 2);
            b.Property(x => x.RemainingDebt).HasPrecision(18, 2);

            // K3 — optimistic concurrency via PostgreSQL system column xmin.
            // Stops PayAsync, CancelSale's debt-close, and partial-return paths
            // from silently overwriting each other's RemainingDebt.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            b.HasOne(x => x.Sale).WithOne(x => x.Debt).HasForeignKey<Debt>(x => x.SaleId)
                .OnDelete(DeleteBehavior.Cascade); // Sale o'chirilsa, Debt ham o'chadi

            // IMPORTANT: Customer o'chirilsa, Debt tarixi o'CHMASIN kerak
            b.HasOne(x => x.Customer).WithMany(p => p.Debts).HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Market).WithMany(m => m.Debts).HasForeignKey(x => x.MarketId);

            // Index for performance
            b.HasIndex(x => new { x.CustomerId, x.Status })
                .HasDatabaseName("IX_Debt_Customer_Status");
            b.HasIndex(x => x.MarketId);
            b.HasIndex(x => x.SaleId)
                .HasDatabaseName("IX_Debt_SaleId");
        });

        // Configure Shift — seller work sessions
        modelBuilder.Entity<Shift>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OpenedAt).IsRequired();
            b.Property(x => x.OpeningCash).HasPrecision(18, 2);
            b.Property(x => x.CountedCash).HasPrecision(18, 2);
            b.Property(x => x.Discrepancy).HasPrecision(18, 2);

            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict); // keep shift history if a user is removed
            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);

            b.HasIndex(x => x.MarketId);
            // Fast "is there an open shift for this user" lookup.
            b.HasIndex(x => new { x.UserId, x.ClosedAt });
            // Per-market Смена № — ordering/lookup helper. Non-unique for the same
            // reason as SaleNumber: allocation is already serialised by the
            // per-market advisory lock (MarketSequenceLock) in ShiftService.
            b.HasIndex(x => new { x.MarketId, x.ShiftNumber });
        });

        // Configure DebtAuditLog
        modelBuilder.Entity<DebtAuditLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.OldPrice).HasPrecision(18, 2);
            b.Property(x => x.NewPrice).HasPrecision(18, 2);
            b.Property(x => x.Comment).IsRequired().HasMaxLength(500);

            // IMPORTANT: Audit log tarixi hech qachon o'CHMASIN kerak
            b.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.SaleItem).WithMany().HasForeignKey(x => x.SaleItemId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict); // User o'chirilsa, audit log qoladi

            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);

            // Indexes for performance
            b.HasIndex(x => new { x.SaleId, x.CreatedAt })
                .HasDatabaseName("IX_DebtAuditLog_Sale_CreatedAt");
            b.HasIndex(x => new { x.SaleItemId, x.CreatedAt })
                .HasDatabaseName("IX_DebtAuditLog_SaleItem_CreatedAt");
            b.HasIndex(x => x.ChangedByUserId)
                .HasDatabaseName("IX_DebtAuditLog_ChangedBy");
            b.HasIndex(x => x.MarketId);
        });

        // Configure Zakup
        modelBuilder.Entity<StockMovement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Delta).HasPrecision(18, 3);
            b.Property(x => x.ResultingQty).HasPrecision(18, 3);
            b.Property(x => x.Type).HasConversion<int>();

            // Product/User o'chirilsa ham harakat tarixi qolsin (audit).
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);

            // "Движение товара" — bitta tovar bo'yicha, eng yangisi birinchi.
            b.HasIndex(x => new { x.MarketId, x.ProductId, x.CreatedAt });
        });

        modelBuilder.Entity<Zakup>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.CostPrice).HasPrecision(18, 2);

            // IMPORTANT: Product o'chirilganda Zakup tarixi o'CHMASIN kerak
            b.HasOne(x => x.Product).WithMany(p => p.Zakups).HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.CreatedByAdmin).WithMany(p => p.Zakups).HasForeignKey(x => x.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict); // User o'chirilsa, Zakup tarixi qoladi

            b.HasOne(x => x.Market).WithMany(m => m.Zakups).HasForeignKey(x => x.MarketId);
            b.HasIndex(x => x.MarketId);

            // Goods-receipt grouping. Deleting a receipt cascades its lines away
            // (the service reverses stock per line first). ReceiptId is nullable
            // only for the brief pre-migration window; the back-fill sets it.
            b.HasOne(x => x.Receipt).WithMany(r => r.Items).HasForeignKey(x => x.ReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ReceiptId);
        });

        // Configure Supplier — goods supplier directory (mirrors Customer).
        modelBuilder.Entity<Supplier>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Phone).HasMaxLength(20);
            b.Property(x => x.Address).HasMaxLength(300);
            b.Property(x => x.Comment).HasMaxLength(500);
            // Soft-delete + multi-tenancy backstop folded into one filter.
            b.HasQueryFilter(x => !x.IsDeleted && (!TenantMarketId.HasValue || x.MarketId == TenantMarketId));

            // Multi-tenancy
            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);
            b.HasIndex(x => x.MarketId);
            b.HasIndex(x => new { x.MarketId, x.Name });
        });

        // Configure ZakupReceipt — goods-receipt header (priyomka).
        modelBuilder.Entity<ZakupReceipt>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.InvoiceNumber).HasMaxLength(100);
            b.Property(x => x.Comment).HasMaxLength(500);
            // Per-market sequential receipt number (№) — ordering/lookup helper.
            // Allocation is collision-free via a per-market advisory lock
            // (MarketSequenceLock) in ZakupService before the max+1 read.
            b.HasIndex(x => new { x.MarketId, x.ReceiptNumber });
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.PaidAmount).HasPrecision(18, 2);
            b.Property(x => x.PaymentStatus).HasConversion<int>().IsRequired();

            // Optimistic concurrency via PostgreSQL system column xmin — stops
            // concurrent supplier-payment updates from clobbering PaidAmount.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // Supplier optional; if the supplier row is removed, keep the receipt
            // history and just null the link.
            b.HasOne(x => x.Supplier).WithMany(s => s.Receipts).HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.SetNull);

            // User o'chirilsa, priyomka tarixi qoladi.
            b.HasOne(x => x.CreatedByAdmin).WithMany().HasForeignKey(x => x.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId);
            b.HasIndex(x => x.MarketId);
            b.HasIndex(x => x.SupplierId);
            // History screen: per-market, newest first.
            b.HasIndex(x => new { x.MarketId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_ZakupReceipt_Market_CreatedAt_Desc");
        });

        // Configure AuditLog
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
            b.Property(x => x.Action).IsRequired().HasMaxLength(50);
            b.Property(x => x.Payload);
            b.Property(x => x.IpAddress).HasMaxLength(64);

            // IMPORTANT: Audit log tarixi hech qachon o'CHMASIN kerak.
            // UserId is optional — an anonymous event (failed login where the
            // username didn't resolve to any user) carries NULL. Restrict (not
            // SetNull) is deliberate: paired with the append-only trigger added
            // in the AuditLogImmutability migration, no FK-cascade UPDATE can
            // ever rewrite an audit row. A user with audit history therefore
            // cannot be hard-deleted; UserService.DeleteUserAsync soft-deletes.
            b.HasOne(x => x.User).WithMany(p => p.AuditLogs).HasForeignKey(x => x.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes for performance
            b.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt })
                .HasDatabaseName("IX_AuditLog_Entity_CreatedAt");
            b.HasIndex(x => new { x.UserId, x.CreatedAt })
                .HasDatabaseName("IX_AuditLog_User_CreatedAt");
            // P5 — security-journal screen does `WHERE MarketId = ? [+
            // filters] ORDER BY CreatedAt DESC LIMIT page`. The old single
            // (MarketId) index narrowed rows but PG still had to sort —
            // expensive once a market accumulates 100k+ audit rows. The
            // composite drives an index-only range scan in reverse-date
            // order. Anonymous events (MarketId NULL) live outside this
            // index and are still served by the (EntityType, EntityId,
            // CreatedAt) one.
            b.HasIndex(x => new { x.MarketId, x.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_AuditLog_Market_CreatedAt_Desc");
        });

        // Configure RefreshToken
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Token).IsRequired().HasMaxLength(500);
            b.Property(x => x.ExpiresAt).IsRequired();
            b.Property(x => x.IsUsed).IsRequired();
            b.Property(x => x.IsRevoked).IsRequired();
            b.Property(x => x.DeviceInfo).HasMaxLength(120);
            b.Property(x => x.IpAddress).HasMaxLength(64);

            // Sessiya zanjirining boshlanish vaqti — mutlaq sessiya umri uchun.
            // Eski qatorlarda 0001-01-01 emas, CreatedAt bo'lishi kerak; buni
            // migration back-fill qiladi.
            b.Property(x => x.SessionStartedAt).IsRequired();

            // Indexes for performance
            b.HasIndex(x => x.Token)
                .HasDatabaseName("IX_RefreshToken_Token");
            b.HasIndex(x => new { x.UserId, x.ExpiresAt })
                .HasDatabaseName("IX_RefreshToken_User_ExpiresAt");
        });

        // Configure CashRegister
        modelBuilder.Entity<CashRegister>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.CurrentBalance).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.LastUpdated).IsRequired();
            b.HasIndex(x => x.LastUpdated);

            // K2 — optimistic concurrency via PostgreSQL system column xmin.
            // No DDL needed — xmin exists on every PG table. Stops concurrent
            // AddCash / WithdrawCash from silently clobbering each other.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // Multi-tenancy: each Market has exactly one CashRegister.
            b.Property(x => x.MarketId).IsRequired();
            b.HasOne(x => x.Market).WithOne(m => m.CashRegister).HasForeignKey<CashRegister>(x => x.MarketId);
            b.HasIndex(x => x.MarketId).IsUnique();
        });

        // Configure CashWithdrawal
        modelBuilder.Entity<CashWithdrawal>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.Comment).IsRequired().HasMaxLength(500);
            b.Property(x => x.WithdrawalDate).IsRequired();
            b.Property(x => x.MarketId).IsRequired();

            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            // Tenant scope. Restrict instead of Cascade so a Market hard-delete
            // never silently rewrites cash history; soft-deactivate the market
            // first. Composite index makes the per-market list query cheap.
            b.HasOne(x => x.Market).WithMany().HasForeignKey(x => x.MarketId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.WithdrawalDate);
            b.HasIndex(x => new { x.MarketId, x.WithdrawalDate });
        });

        // Configure RegistrationRequest
        modelBuilder.Entity<RegistrationRequest>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            b.Property(x => x.Phone).IsRequired().HasMaxLength(20);
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.RejectReason).HasMaxLength(500);

            // Optimistic concurrency via PostgreSQL system column xmin.
            // Surfaces concurrent approvals as DbUpdateConcurrencyException → 409.
            b.Property(x => x.Xmin)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // SuperAdmin lists are typically filtered by Status and ordered by date.
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.CreatedAt);
            // Partial unique on Phone WHERE Status=Pending (0) prevents two parallel
            // submissions from a single phone landing as duplicate pending rows.
            // Rejected/Approved rows from the same phone are allowed — the applicant
            // can re-apply after a rejection.
            b.HasIndex(x => x.Phone)
                .IsUnique()
                .HasFilter("\"Status\" = 0")
                .HasDatabaseName("IX_RegistrationRequests_Phone_Pending");

            // Linkage to the artifacts we create on approval. Use Restrict so a user
            // can't be cascade-deleted while a historical request still references it.
            b.HasOne(x => x.ProcessedByUser).WithMany().HasForeignKey(x => x.ProcessedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedUser).WithMany().HasForeignKey(x => x.CreatedUserId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CreatedMarket).WithMany().HasForeignKey(x => x.CreatedMarketId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Multi-tenancy backstop (defense-in-depth) ───────────────────────
        // Every tenant-owned entity gets a global query filter so a read is
        // auto-scoped to the caller's market even if a service forgets its
        // manual `.Where(x => x.MarketId == marketId)`. TenantMarketId is null
        // when there is no market context (SuperAdmin, login, startup, design-
        // time) — the filter is then a no-op and returns all rows.
        //
        // The soft-delete entities (Customer/User/Product/ProductCategory/Sale/
        // Supplier) already fold this same predicate into their !IsDeleted
        // filter above. The entities below carry no soft-delete filter, so the
        // tenant predicate is their only global filter. RegistrationRequest is
        // intentionally excluded — it is a pre-market signup row keyed on
        // CreatedMarketId and is managed cross-tenant by SuperAdmin.
        //
        // The FOR UPDATE lock queries (FromSqlInterpolated in SaleService/
        // DebtService) each re-check MarketId in code right after loading, so
        // tenant safety does not depend on whether EF composes this filter over
        // their raw SQL.
        modelBuilder.Entity<Payment>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<Debt>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<Shift>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<DebtAuditLog>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<Zakup>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<ZakupReceipt>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<StockMovement>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<CashRegister>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
        modelBuilder.Entity<CashWithdrawal>().HasQueryFilter(x => !TenantMarketId.HasValue || x.MarketId == TenantMarketId);
    }
}
