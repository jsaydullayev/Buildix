using Microsoft.EntityFrameworkCore;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Common;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Item concern extracted from SaleService: add / remove a sale line, edit a
/// line price, validate a line price. Stock movement, per-market checks, the
/// FOR UPDATE product lock and the SUM-based total live here. See
/// <see cref="ISaleItemService"/>. Customer-credit re-application is delegated
/// to <see cref="ISaleCreditApplier"/>; total recompute to <see cref="SaleTotals"/>.
/// </summary>
public class SaleItemService : ISaleItemService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly ILogger<SaleItemService> _logger;
    private readonly ISaleCreditApplier _creditApplier;
    private readonly IMarketSettingsService _settings;
    private readonly IAuditLogService _auditLogService;

    public SaleItemService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService, ILogger<SaleItemService> logger, ISaleCreditApplier creditApplier, IMarketSettingsService settings, IAuditLogService auditLogService)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _logger = logger;
        _creditApplier = creditApplier;
        _settings = settings;
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// True when MarketSettings.BlockSaleBelowCost is on AND the sale's seller is
    /// a Seller-role user (Admin/Owner are exempt — they may discount below cost).
    /// </summary>
    private async Task<bool> BelowCostBlockedForAsync(Sale sale, int marketId, CancellationToken ct)
    {
        var settings = await _settings.GetOrCreateAsync(marketId, ct);
        if (!settings.BlockSaleBelowCost) return false;
        var sellerRole = await _context.Users
            .Where(u => u.Id == sale.SellerId)
            .Select(u => (Role?)u.Role)
            .FirstOrDefaultAsync(ct);
        return sellerRole == Role.Seller;
    }

    public async Task<Result<SaleItemDto>> AddSaleItemAsync(Guid saleId, AddSaleItemDto request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
            return Result.Failure<SaleItemDto>("Quantity must be greater than 0");
        if (request.SalePrice < 0)
            return Result.Failure<SaleItemDto>("Sale price cannot be negative");

        var marketId = _currentMarketService.GetCurrentMarketId();

        // LOG: Track IsExternal flag
        _logger.LogInformation("[AddSaleItem] RECEIVED - SaleId: {SaleId}, IsExternal: {IsExternal}, ProductId: {ProductId}, ExternalProductName: {ProductName}, Quantity: {Quantity}",
            saleId, request.IsExternal, request.ProductId, request.ExternalProductName, request.Quantity);

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Get sale with MarketId filtering
            var sales = await _unitOfWork.Sales.FindAsync(
                s => s.Id == saleId && s.MarketId == marketId,
                cancellationToken);
            var sale = sales.FirstOrDefault();

            if (sale is null || sale.Status != SaleStatus.Draft)
                return Result.Failure<SaleItemDto>("Sale not found or not in Draft status");

            // Load sale items separately
            var saleItems = await _unitOfWork.SaleItems.FindAsync(si => si.SaleId == saleId, cancellationToken);

            if (!request.IsExternal)
            {
                // ------------ ORDINARY PRODUCT (Oddiy mahsulot) ------------
                // ProductId bo'lishi shart
                if (request.ProductId == null)
                    return Result.Failure<SaleItemDto>("ProductId kerak (oddiy mahsulot uchun)");

                var productId = request.ProductId.Value;
                // FOR UPDATE faqat PostgreSQL da ishlaydi; InMemory test DB da oddiy query
                Product? product;
                if (_context.Database.ProviderName?.Contains("InMemory") == false)
                {
                    // EF Core wraps this query and references xmin (the concurrency
                    // token on Product). PostgreSQL doesn't include xmin in `SELECT *`,
                    // so we must list it explicitly — otherwise we get
                    // `42703: column m.xmin does not exist`.
                    product = await _context.Products
                        .FromSqlInterpolated($"SELECT *, xmin FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
                        .FirstOrDefaultAsync(cancellationToken);
                }
                else
                {
                    product = await _unitOfWork.Products.GetByIdAsync(productId, cancellationToken);
                }
                if (product is null)
                    return Result.Failure<SaleItemDto>("Product not found");

                // SECURITY: Verify product belongs to same market as sale
                if (product.MarketId != sale.MarketId)
                    return Result.Failure<SaleItemDto>("Product does not belong to this market");

                // Business rule: block selling below cost (Sellers only).
                if (request.SalePrice < product.CostPrice
                    && await BelowCostBlockedForAsync(sale, marketId, cancellationToken))
                    return Result.Failure<SaleItemDto>(
                        "Цена продажи ниже закупочной.", "BELOW_COST");

                // Validate stock
                if (product.Quantity <= 0)
                    return Result.Failure<SaleItemDto>("Bu mahsulot omborda yo'q");

                if (product.Quantity < request.Quantity)
                    return Result.Failure<SaleItemDto>($"Omborda yetarli mahsulot yo'q. Mavjud: {product.Quantity}, So'ralgan: {request.Quantity}");

                // Note: MinSalePrice validation is now UI-only warning, not enforced on backend
                // Sellers can sell below minimum price without comment if needed

                // Check threshold (warning only, not blocking)
                if (product.Quantity <= product.MinThreshold)
                {
                    // Log warning - product is at or below threshold
                    // This is allowed but should trigger warning in UI
                }

                SaleItem? resultSaleItem;
                decimal itemTotal;

                // CHECK: Is this product already in sale?
                var existingItem = saleItems.FirstOrDefault(si => si.ProductId == request.ProductId);

                if (existingItem != null)
                {
                    // Product exists - UPDATE existing item
                    var oldQuantity = existingItem.Quantity;
                    existingItem.Quantity += request.Quantity;

                    // LOG: Existing item update
                    _logger.LogInformation("[AddSaleItem] UPDATE EXISTING - OldQty: {OldQty}, RequestQty: {RequestQty}, NewQty: {NewQty}",
                        oldQuantity, request.Quantity, existingItem.Quantity);

                    _unitOfWork.SaleItems.Update(existingItem);

                    // Update stock
                    product.Quantity -= request.Quantity;
                    _unitOfWork.Products.Update(product);

                    itemTotal = existingItem.Quantity * existingItem.SalePrice;
                    resultSaleItem = existingItem;
                }
                else
                {
                    // Product doesn't exist - CREATE new item
                    var saleItem = new SaleItem
                    {
                        Id = Guid.NewGuid(),
                        SaleId = saleId,
                        ProductId = request.ProductId,
                        IsExternal = false,  // ✅ Oddiy mahsulot
                        Quantity = request.Quantity,
                        CostPrice = product.CostPrice,
                        SalePrice = request.SalePrice,
                        Comment = request.Comment
                    };

                    // LOG: New item create
                    _logger.LogInformation("[AddSaleItem] CREATE NEW - Quantity: {Quantity}, ProductId: {ProductId}",
                        saleItem.Quantity, saleItem.ProductId);

                    await _unitOfWork.SaleItems.AddAsync(saleItem, cancellationToken);

                    // Update stock
                    product.Quantity -= request.Quantity;
                    _unitOfWork.Products.Update(product);

                    itemTotal = request.Quantity * request.SalePrice;
                    resultSaleItem = saleItem;
                }

                // M9 — the two SaveChanges below are intentional and BOTH
                // required for correctness:
                //
                //   1st SaveChanges: persists the new/updated SaleItem +
                //      Product stock change to the DB so the row(s) become
                //      visible to subsequent DB-side queries in this txn.
                //   RecalculateSaleTotalAsync: runs a SERVER-side SUM over
                //      SaleItems (not the in-memory tracked set), so it
                //      sees the row we just persisted plus anything
                //      another concurrent transaction has already committed.
                //   2nd SaveChanges: writes the authoritative TotalAmount.
                //
                // The old "math" approach (`sale.TotalAmount += newQty * newPrice`)
                // collapsed under concurrency because two callers could each
                // read the same stale total and overwrite each other's delta.
                // The Sale.Xmin concurrency token would catch the conflict on
                // commit, but recovery requires reloading the entity and
                // re-running the math — which the DB-SUM approach gives us
                // for free. Net: 1 extra round trip in exchange for race-free
                // totals.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await RecalculateSaleTotalAsync(sale, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // After the item lands and the sale total grows, re-apply any outstanding
                // customer credit so the new portion of the bill is automatically covered.
                if (sale.CustomerId.HasValue)
                {
                    await _creditApplier.ApplyAsync(sale.Id, sale.CustomerId.Value, cancellationToken);
                }

                // LOG: After DB save
                _logger.LogInformation("[AddSaleItem] AFTER DB SAVE - Quantity: {Quantity}, ProductId: {ProductId}",
                    resultSaleItem.Quantity, resultSaleItem.ProductId);

                return Result.Success(SaleMapper.MapItem(resultSaleItem, product.Name, product.GetUnitName(), (int)product.Unit));
            }
            else
            {
                // ------------ EXTERNAL PRODUCT (Tashqi mahsulot) ------------
                // ExternalProductName bo'lishi shart
                if (string.IsNullOrEmpty(request.ExternalProductName))
                    return Result.Failure<SaleItemDto>("ExternalProductName kerak (tashqi mahsulot uchun)");

                // ExternalCostPrice is nullable on the DTO but mandatory when IsExternal
                // is true — without this guard the .Value access below NREs.
                if (!request.ExternalCostPrice.HasValue)
                    return Result.Failure<SaleItemDto>("ExternalCostPrice kerak (tashqi mahsulot uchun)");
                if (request.ExternalCostPrice.Value < 0)
                    return Result.Failure<SaleItemDto>("Tashqi tannarx manfiy bo'lmasin");

                // ✅ VALIDATION: Tashqi tannarx sotuv narxidan katta bo'lishi mumkin emas
                if (request.ExternalCostPrice.Value >= request.SalePrice)
                    return Result.Failure<SaleItemDto>("Tashqi tannarx sotuv narxidan katta yoki teng bo'lishi mumkin emas");

                SaleItem? resultSaleItem;
                decimal itemTotal;

                // CHECK: Is this external product already in sale? (by name)
                var existingItem = saleItems.FirstOrDefault(si =>
                    si.IsExternal &&
                    si.ExternalProductName == request.ExternalProductName);

                if (existingItem != null)
                {
                    // External product exists - UPDATE existing item
                    var oldQuantity = existingItem.Quantity;
                    existingItem.Quantity += request.Quantity;

                    // LOG: Existing item update
                    _logger.LogInformation("[AddSaleItem] UPDATE EXTERNAL - OldQty: {OldQty}, RequestQty: {RequestQty}, NewQty: {NewQty}",
                        oldQuantity, request.Quantity, existingItem.Quantity);

                    _unitOfWork.SaleItems.Update(existingItem);

                    itemTotal = existingItem.Quantity * existingItem.SalePrice;
                    resultSaleItem = existingItem;
                }
                else
                {
                    // External product doesn't exist - CREATE new item
                    var saleItem = new SaleItem
                    {
                        Id = Guid.NewGuid(),
                        SaleId = saleId,
                        IsExternal = true,  // ✅ Tashqi mahsulot
                        ProductId = null,  // ✅ Nullable
                        ExternalProductName = request.ExternalProductName,
                        ExternalCostPrice = request.ExternalCostPrice.Value,
                        Quantity = request.Quantity,
                        SalePrice = request.SalePrice,
                        Comment = request.Comment
                    };

                    // LOG: New item create
                    _logger.LogInformation("[AddSaleItem] CREATE EXTERNAL - Quantity: {Quantity}, ProductName: {ProductName}",
                        saleItem.Quantity, request.ExternalProductName);

                    await _unitOfWork.SaleItems.AddAsync(saleItem, cancellationToken);

                    // ✅ NO STOCK UPDATE - Tashqi mahsulotlar ombor qoldig'iga ta'sir qilmaydi

                    itemTotal = request.Quantity * request.SalePrice;
                    resultSaleItem = saleItem;
                }

                // Same SUM-from-items recompute as the ordinary branch.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await RecalculateSaleTotalAsync(sale, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // External items also count toward the bill, so re-apply credit.
                if (sale.CustomerId.HasValue)
                {
                    await _creditApplier.ApplyAsync(sale.Id, sale.CustomerId.Value, cancellationToken);
                }

                // LOG: After DB save
                _logger.LogInformation("[AddSaleItem] AFTER DB SAVE - IsExternal: {IsExternal}, ProductName: {ProductName}, Quantity: {Quantity}",
                    resultSaleItem.IsExternal, resultSaleItem.ExternalProductName, resultSaleItem.Quantity);

                // Mapping: Product name = ExternalProductName, Unit = empty
                return Result.Success(SaleMapper.MapItem(
                    resultSaleItem,
                    resultSaleItem.ExternalProductName ?? "Unknown",
                    ""
                ));
            }
        }, cancellationToken);
    }

    public async Task<Result<SaleItemDto>> RemoveSaleItemAsync(Guid saleId, RemoveSaleItemDto request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
            return Result.Failure<SaleItemDto>("Quantity must be greater than 0");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Get sale with MarketId filtering
            var sales = await _unitOfWork.Sales.FindAsync(
                s => s.Id == saleId && s.MarketId == marketId,
                cancellationToken);
            var sale = sales.FirstOrDefault();

            if (sale is null || sale.Status != SaleStatus.Draft)
                return Result.Failure<SaleItemDto>("Sale not found or not in Draft status");

            // Get sale item
            var saleItemGuid = Guid.Parse(request.SaleItemId);
            var saleItems = await _unitOfWork.SaleItems.FindAsync(
                si => si.Id == saleItemGuid && si.SaleId == saleId,
                cancellationToken);
            var saleItem = saleItems.FirstOrDefault();

            if (saleItem == null)
                return Result.Failure<SaleItemDto>("Sale item not found");

            /// <summary>
            /// ============================================
            /// ✅ ISEXTERNAL SHARTI - STOKNI SAQLASH
            /// ============================================
            /// </summary>
            if (!saleItem.IsExternal)
            {
                // ---- ORDINARY PRODUCT (Oddiy mahsulot) ----
                // ProductId bo'lishi shart
                if (saleItem.ProductId == null)
                    return Result.Failure<SaleItemDto>("ProductId null (oddiy mahsulot uchun)");

                var product = await _unitOfWork.Products.GetByIdAsync(saleItem.ProductId.Value, cancellationToken);
                if (product is null)
                    return Result.Failure<SaleItemDto>("Product not found");

                // SECURITY: Verify product belongs to same market as sale
                if (product.MarketId != sale.MarketId)
                    return Result.Failure<SaleItemDto>("Product does not belong to this market");

                SaleItem? resultSaleItem;
                decimal itemTotal;

                if (request.Quantity == 0 || request.Quantity >= saleItem.Quantity)
                {
                    // Remove entire item from sale
                    _unitOfWork.SaleItems.Delete(saleItem);

                    // ✅ Restore full stock (faqat oddiy mahsulotlar uchun)
                    product.Quantity += saleItem.Quantity;
                    _unitOfWork.Products.Update(product);

                    itemTotal = saleItem.Quantity * saleItem.SalePrice;
                    resultSaleItem = saleItem; // Return deleted item info
                }
                else
                {
                    // Partial quantity removal
                    saleItem.Quantity -= request.Quantity;
                    _unitOfWork.SaleItems.Update(saleItem);

                    // ✅ Restore partial stock (faqat oddiy mahsulotlar uchun)
                    product.Quantity += request.Quantity;
                    _unitOfWork.Products.Update(product);

                    itemTotal = saleItem.Quantity * saleItem.SalePrice;
                    resultSaleItem = saleItem;
                }

                // SUM-from-items recompute — matches AddSaleItem (kills the
                // same race condition under concurrent remove + add).
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await RecalculateSaleTotalAsync(sale, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success(SaleMapper.MapItem(resultSaleItem, product.Name, product.GetUnitName(), (int)product.Unit));
            }
            else
            {
                // ---- EXTERNAL PRODUCT (Tashqi mahsulot) ----
                // ✅ NO STOCK RESTORE - Tashqi mahsulotlar ombor qoldig'iga ta'sir qilmaydi

                SaleItem? resultSaleItem;
                decimal itemTotal;

                if (request.Quantity == 0 || request.Quantity >= saleItem.Quantity)
                {
                    // Remove entire item from sale
                    _unitOfWork.SaleItems.Delete(saleItem);
                    itemTotal = saleItem.Quantity * saleItem.SalePrice;
                    resultSaleItem = saleItem;
                }
                else
                {
                    // Partial quantity removal
                    saleItem.Quantity -= request.Quantity;
                    _unitOfWork.SaleItems.Update(saleItem);
                    itemTotal = saleItem.Quantity * saleItem.SalePrice;
                    resultSaleItem = saleItem;
                }

                // SUM-from-items recompute.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await RecalculateSaleTotalAsync(sale, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // Mapping: Product name = ExternalProductName, Unit = empty
                return Result.Success(SaleMapper.MapItem(
                    saleItem,
                    saleItem.ExternalProductName ?? "Unknown",
                    ""
                ));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Set a line to an EXACT quantity. See <see cref="ISaleItemService.SetSaleItemQuantityAsync"/>.
    ///
    /// Deliberately NOT expressed as "Add(delta) or Remove(-delta)" on the client:
    /// the delta would be computed from a possibly stale client-side quantity, so
    /// two quick edits could double-apply. Here the difference is taken from the
    /// row the transaction just read under the product's FOR UPDATE lock, which
    /// makes the outcome independent of what the client believed.
    /// </summary>
    public async Task<Result<SaleItemDto>> SetSaleItemQuantityAsync(Guid saleId, SetSaleItemQuantityDto request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity < 0)
            return Result.Failure<SaleItemDto>("Miqdor manfiy bo'lmasin");

        if (!Guid.TryParse(request.SaleItemId, out var saleItemGuid))
            return Result.Failure<SaleItemDto>("Noto'g'ri saleItemId formati.");

        // Column is decimal(18,3). A 4-decimal input is rounded HERE, before the
        // stock math runs — not rejected. Rounding first is what keeps the line,
        // the stock movement and the stored row talking about the same number:
        // let the un-rounded value drive the math and the DB would silently
        // truncate it afterwards, leaving stock off by the discarded digits.
        var newQuantity = Math.Round(request.Quantity, 3, MidpointRounding.AwayFromZero);

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var sales = await _unitOfWork.Sales.FindAsync(
                s => s.Id == saleId && s.MarketId == marketId,
                cancellationToken);
            var sale = sales.FirstOrDefault();

            if (sale is null || sale.Status != SaleStatus.Draft)
                return Result.Failure<SaleItemDto>("Sale not found or not in Draft status");

            var saleItems = await _unitOfWork.SaleItems.FindAsync(
                si => si.Id == saleItemGuid && si.SaleId == saleId,
                cancellationToken);
            var saleItem = saleItems.FirstOrDefault();

            if (saleItem is null)
                return Result.Failure<SaleItemDto>("Sale item not found");

            // How far the stock has to move. Ordinary lines recompute this from a
            // post-lock read below; external lines move no stock at all.
            var delta = newQuantity - saleItem.Quantity;

            string productName;
            string unitName = "";
            int unitValue = 0;

            if (!saleItem.IsExternal)
            {
                if (saleItem.ProductId is null)
                    return Result.Failure<SaleItemDto>("ProductId null (oddiy mahsulot uchun)");

                var productId = saleItem.ProductId.Value;
                // Same FOR UPDATE lock as AddSaleItemAsync — see the note there on
                // why xmin has to be listed explicitly.
                Product? product;
                if (_context.Database.ProviderName?.Contains("InMemory") == false)
                {
                    product = await _context.Products
                        .FromSqlInterpolated($"SELECT *, xmin FROM \"Products\" WHERE \"Id\" = {productId} FOR UPDATE")
                        .FirstOrDefaultAsync(cancellationToken);
                }
                else
                {
                    product = await _unitOfWork.Products.GetByIdAsync(productId, cancellationToken);
                }

                if (product is null)
                    return Result.Failure<SaleItemDto>("Product not found");

                if (product.MarketId != sale.MarketId)
                    return Result.Failure<SaleItemDto>("Product does not belong to this market");

                // Re-read the line's quantity AFTER the lock, not before.
                //
                // SaleItem carries no concurrency token, so a quantity read
                // before the lock is just a snapshot: two concurrent set-calls
                // would each compute their difference from the same stale value
                // and the second would move the wrong amount of stock (set 5 and
                // set 10 from 2 ⇒ 11 units taken for a line of 10). Waiting on
                // the product lock first serialises the pair, and this scalar
                // projection goes to the database rather than the change
                // tracker, so it sees whatever the other transaction committed.
                var currentQuantity = await _context.SaleItems
                    .Where(si => si.Id == saleItemGuid)
                    .Select(si => (decimal?)si.Quantity)
                    .FirstOrDefaultAsync(cancellationToken);

                // Gone while we waited on the lock — the other transaction
                // removed the line, and it took its stock back with it.
                if (currentQuantity is null)
                    return Result.Failure<SaleItemDto>("Sale item not found");

                delta = newQuantity - currentQuantity.Value;

                // Only an INCREASE can run out of stock; shrinking a line always
                // gives stock back.
                if (delta > 0 && product.Quantity < delta)
                    return Result.Failure<SaleItemDto>(
                        $"Omborda yetarli mahsulot yo'q. Mavjud: {product.Quantity}, So'ralgan: {delta}");

                // One expression covers both directions: positive delta takes
                // stock, negative delta returns it.
                product.Quantity -= delta;
                _unitOfWork.Products.Update(product);

                productName = product.Name;
                unitName = product.GetUnitName();
                unitValue = (int)product.Unit;
            }
            else
            {
                // External lines never touch stock — nothing to move.
                productName = saleItem.ExternalProductName ?? "Unknown";
            }

            if (newQuantity == 0)
            {
                // Zero it before deleting so the returned DTO reads 0 rather than
                // the pre-delete quantity — a client that trusts the response
                // would otherwise re-render a line that no longer exists. EF ends
                // on Deleted, so the assignment never reaches the DB.
                saleItem.Quantity = 0;
                _unitOfWork.SaleItems.Delete(saleItem);
            }
            else
            {
                saleItem.Quantity = newQuantity;
                _unitOfWork.SaleItems.Update(saleItem);
            }

            // Same persist → SUM-from-DB → persist pattern as AddSaleItemAsync;
            // see the long note there on why the total is not computed in memory.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RecalculateSaleTotalAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The bill moved, so any outstanding customer credit has to be
            // re-applied against the new total.
            if (sale.CustomerId.HasValue)
            {
                await _creditApplier.ApplyAsync(sale.Id, sale.CustomerId.Value, cancellationToken);
            }

            return Result.Success(saleItem.IsExternal
                ? SaleMapper.MapItem(saleItem, productName, "")
                : SaleMapper.MapItem(saleItem, productName, unitName, unitValue));
        }, cancellationToken);
    }

    public async Task<bool> ValidateSalePriceAsync(Guid saleItemId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var saleItems = await _unitOfWork.SaleItems.FindAsync(
            si => si.Id == saleItemId,
            cancellationToken);
        var saleItem = saleItems.FirstOrDefault();

        if (saleItem is null)
            return false;

        // Get sale to verify market
        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.Id == saleItem.SaleId && s.MarketId == marketId,
            cancellationToken);
        if (!sales.Any())
            return false;

        // External products have no MinSalePrice constraint — always valid.
        if (saleItem.IsExternal)
            return true;

        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == saleItem.ProductId && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return false;

        // Returns true if price is valid (>= min price) or comment is provided
        return saleItem.SalePrice >= product.MinSalePrice || !string.IsNullOrWhiteSpace(saleItem.Comment);
    }

    public async Task<Result<SaleItemDto>> UpdateSaleItemPriceAsync(Guid saleItemId, UpdateSaleItemPriceDto request, Guid userId, CancellationToken cancellationToken = default)
    {
        // S2 — guard against negative prices at the entry point. The
        // recalculated Sale.TotalAmount would otherwise silently go negative
        // and break every downstream report that sums TotalAmount.
        if (request.NewPrice < 0)
            return Result.Failure<SaleItemDto>("Narx manfiy bo'lmasin");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var saleItems = await _unitOfWork.SaleItems.FindAsync(
                si => si.Id == saleItemId, q => q.Include(e => e.Sale), cancellationToken);
            var saleItem = saleItems.FirstOrDefault();

            if (saleItem == null)
                return Result.Failure<SaleItemDto>("SaleItem topilmadi");

            var sale = saleItem.Sale;
            if (sale == null || sale.MarketId != marketId)
                return Result.Failure<SaleItemDto>("Sotuv topilmadi");

            // S2 — refuse to mutate prices on a finalised sale. Previously the
            // method would happily overwrite SalePrice on a Paid / Debt /
            // Cancelled sale, corrupting the historic financial total even
            // though Sale.Xmin would block the eventual save — that's a
            // 500 to the user instead of a clean 400. Status check first.
            if (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Debt)
                return Result.Failure<SaleItemDto>(
                    "Narxni faqat Draft yoki Qarz holatidagi sotuvlarda o'zgartirish mumkin");

            // Business rule: block editing a line price below cost (Sellers only).
            var lineCost = saleItem.IsExternal ? saleItem.ExternalCostPrice : saleItem.CostPrice;
            if (request.NewPrice < lineCost
                && await BelowCostBlockedForAsync(sale, marketId, cancellationToken))
                return Result.Failure<SaleItemDto>("Цена продажи ниже закупочной.", "BELOW_COST");

            // Update SaleItem price
            var oldPrice = saleItem.SalePrice;
            saleItem.SalePrice = request.NewPrice;
            _unitOfWork.SaleItems.Update(saleItem);

            // Fraud audit: overriding a line price (esp. on a Debt sale) is a
            // direct "sell low / discount to an accomplice" vector — record the
            // actor + old→new price. Staged so it commits with the price change.
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, sale.Id, AuditActions.PriceOverride, userId,
                new
                {
                    SaleId = sale.Id,
                    SaleItemId = saleItemId,
                    OldPrice = oldPrice,
                    NewPrice = request.NewPrice,
                    Status = sale.Status.ToString(),
                    saleItem.IsExternal,
                },
                cancellationToken);

            // S2 — persist the SaleItem change first, then SUM straight from
            // the DB. The old code walked tracked entities in memory which
            // depended on EF identity-resolution semantics; aligning with
            // AddSaleItem's pattern (SaveChanges → RecalculateSaleTotalAsync
            // via SUM → SaveChanges) makes the result deterministic and
            // race-protected by Sale.Xmin.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RecalculateSaleTotalAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (sale.Status == SaleStatus.Debt)
            {
                var debtForPrice = await _context.Debts
                    .FirstOrDefaultAsync(d => d.SaleId == sale.Id && d.MarketId == marketId, cancellationToken);
                if (debtForPrice != null)
                {
                    debtForPrice.TotalDebt = sale.TotalAmount;
                    debtForPrice.RemainingDebt = Math.Max(0, sale.TotalAmount - sale.PaidAmount);
                    if (debtForPrice.RemainingDebt <= 0)
                        debtForPrice.Status = DebtStatus.Closed;
                    _context.Debts.Update(debtForPrice);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // Get product name for response
            string productName;
            string unit = "";

            if (!saleItem.IsExternal)
            {
                if (saleItem.ProductId.HasValue)
                {
                    var product = await _unitOfWork.Products.GetByIdAsync(saleItem.ProductId.Value, cancellationToken);
                    productName = product?.Name ?? "Unknown";
                    unit = product?.GetUnitName() ?? "";
                }
                else
                {
                    productName = "Unknown";
                }
            }
            else
            {
                productName = saleItem.ExternalProductName ?? "Tashqi mahsulot";
                unit = "";
            }

            return Result.Success(new SaleItemDto(
                saleItem.Id.ToString(),
                saleItem.SaleId.ToString(),
                saleItem.ProductId,
                productName,
                saleItem.Quantity,
                saleItem.IsExternal ? saleItem.ExternalCostPrice : saleItem.CostPrice,
                saleItem.SalePrice,
                saleItem.TotalPrice,
                (saleItem.SalePrice - (saleItem.IsExternal ? saleItem.ExternalCostPrice : saleItem.CostPrice)) * saleItem.Quantity,
                unit,
                saleItem.Comment,
                saleItem.IsExternal
            ));
        }, cancellationToken);
    }

    private async Task RecalculateSaleTotalAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        // Shared charged-total formula (SUM of items − discount, clamped at 0).
        await SaleTotals.RecalculateAsync(_context, sale, cancellationToken);
        _unitOfWork.Sales.Update(sale);
    }
}
