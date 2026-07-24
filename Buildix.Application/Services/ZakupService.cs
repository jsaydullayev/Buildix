using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

public class ZakupService : IZakupService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IStockLedger _stockLedger;

    public ZakupService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, IAppDbContext context, ICurrentMarketService currentMarketService, IStockLedger stockLedger)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _context = context;
        _currentMarketService = currentMarketService;
        _stockLedger = stockLedger;
    }

    // ── Single-line reads (unchanged) ────────────────────────────────────────

    public async Task<ZakupDto?> GetZakupByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var zakups = await _unitOfWork.Zakups.FindAsync(
            z => z.Id == id && z.MarketId == marketId,
            cancellationToken);

        var zakup = zakups.FirstOrDefault();

        if (zakup is null)
            return null;

        return await MapToDtoAsync(zakup, cancellationToken);
    }

    public async Task<IEnumerable<ZakupDto>> GetAllZakupsAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var zakups = await _context.Zakups
            .AsNoTracking()
            .Include(z => z.Product)
            .Include(z => z.CreatedByAdmin)
            .Where(z => z.MarketId == marketId)
            .OrderByDescending(z => z.CreatedAt)
            .ToListAsync(cancellationToken);

        return zakups.Select(MapToDtoEager).ToList();
    }

    public async Task<PagedResult<ZakupDto>> GetAllZakupsPagedAsync(int page, int size, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var marketId = _currentMarketService.GetCurrentMarketId();

        var query = _context.Zakups
            .AsNoTracking()
            .Include(z => z.Product)
            .Include(z => z.CreatedByAdmin)
            .Where(z => z.MarketId == marketId);

        var total = await query.CountAsync(cancellationToken);
        var zakups = await query
            .OrderByDescending(z => z.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return PagedResult<ZakupDto>.From(zakups.Select(MapToDtoEager).ToList(), page, size, total);
    }

    public async Task<IEnumerable<ZakupDto>> GetZakupsByDateRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var zakups = await _context.Zakups
            .AsNoTracking()
            .Include(z => z.Product)
            .Include(z => z.CreatedByAdmin)
            .Where(z => z.MarketId == marketId && z.CreatedAt >= start && z.CreatedAt <= end)
            .OrderByDescending(z => z.CreatedAt)
            .ToListAsync(cancellationToken);

        return zakups.Select(MapToDtoEager).ToList();
    }

    // ── Create ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Quick single-product re-stock. Wraps the one line in a 1-item receipt
    /// (no supplier, treated as fully paid) so every purchase — whether from the
    /// quick "+stok" button or the full priyomka sheet — lives under a receipt
    /// and the history has one uniform shape.
    /// </summary>
    public async Task<ZakupDto> CreateZakupAsync(CreateZakupDto request, Guid adminId, CancellationToken cancellationToken = default)
    {
        var lineTotal = request.Quantity * request.CostPrice;

        var receipt = await CreateZakupReceiptAsync(new CreateZakupReceiptDto(
            SupplierId: null,
            InvoiceNumber: null,
            PaidAmount: lineTotal,
            Comment: null,
            Items: new List<CreateZakupLineDto> { new(request.ProductId, request.Quantity, request.CostPrice) }
        ), adminId, cancellationToken);

        var line = receipt.Items[0];
        return new ZakupDto(line.Id, line.ProductId, line.ProductName, line.Quantity, line.CostPrice, receipt.CreatedAt, receipt.CreatedBy);
    }

    /// <summary>
    /// Multi-item goods-receipt (priyomka). Creates one header + N product lines
    /// in a single transaction, adds each line's stock, updates each product's
    /// cost to the latest purchase price, computes the total and the supplier
    /// payment state, and audit-logs the receipt.
    /// </summary>
    public async Task<ZakupReceiptDto> CreateZakupReceiptAsync(CreateZakupReceiptDto request, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("Kamida bitta mahsulot kerak");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Validate the supplier (when supplied) belongs to this market.
            if (request.SupplierId.HasValue)
            {
                var supplierOk = await _context.Suppliers
                    .AnyAsync(s => s.Id == request.SupplierId.Value && s.MarketId == marketId && !s.IsDeleted, cancellationToken);
                if (!supplierOk)
                    throw new InvalidOperationException("Yetkazib beruvchi topilmadi");
            }

            // Per-market ketma-ket priyomka raqami (№). Per-market advisory lock
            // (inside this txn) serialises allocation so two concurrent receipts
            // can't reuse a number — matches the Sale ЧЕК № path.
            await MarketSequenceLock.AcquireAsync(
                _context, MarketSequenceLock.ZakupReceiptNumberClass, marketId, cancellationToken);
            var nextNumber = (await _context.ZakupReceipts
                .Where(r => r.MarketId == marketId)
                .MaxAsync(r => (int?)r.ReceiptNumber, cancellationToken) ?? 0) + 1;

            var receipt = new ZakupReceipt
            {
                Id = Guid.NewGuid(),
                ReceiptNumber = nextNumber,
                SupplierId = request.SupplierId,
                InvoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber) ? null : request.InvoiceNumber.Trim(),
                Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
                CreatedByAdminId = adminId,
                MarketId = marketId,
                CreatedAt = DateTime.UtcNow,
                // «В пути» — stok accept'da kiradi; aks holda darhol (backward-compat).
                DeliveryStatus = request.InTransit ? DeliveryStatus.InTransit : DeliveryStatus.Accepted,
            };
            await _context.ZakupReceipts.AddAsync(receipt, cancellationToken);

            // Load every referenced product up front (market-scoped, tracked so
            // stock/cost mutations persist on SaveChanges) — avoids N+1.
            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id) && p.MarketId == marketId)
                .ToListAsync(cancellationToken);
            var productMap = products.ToDictionary(p => p.Id);

            decimal total = 0m;
            foreach (var line in request.Items)
            {
                if (line.Quantity <= 0)
                    throw new InvalidOperationException("Soni 0 dan katta bo'lishi kerak");
                if (line.CostPrice < 0)
                    throw new InvalidOperationException("Narx manfiy bo'lishi mumkin emas");
                if (!productMap.TryGetValue(line.ProductId, out var product))
                    throw new InvalidOperationException("Mahsulot topilmadi");

                var zakup = new Zakup
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = receipt.Id,
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    CostPrice = line.CostPrice,
                    CreatedByAdminId = adminId,
                    MarketId = marketId,
                    CreatedAt = receipt.CreatedAt,
                };
                await _context.Zakups.AddAsync(zakup, cancellationToken);

                // Stok faqat DARHOL qabulda kiradi. «В пути» bo'lsa tovar hali
                // yo'lda — qoldiq/tannarx/harakat accept'ga qoldiriladi. Total
                // (yetkazuvchi qarzi) ikkala holatda ham hisoblanadi.
                if (!request.InTransit)
                {
                    product.Quantity += line.Quantity;
                    product.CostPrice = line.CostPrice; // latest purchase price
                    // Kelgan tovar — Приход · З-{ReceiptNumber}, bir tranzaksiyada.
                    _stockLedger.Record(product, line.Quantity, StockMovementType.Purchase,
                        refNumber: receipt.ReceiptNumber, userId: adminId);
                }
                total += line.Quantity * line.CostPrice;
            }

            receipt.TotalAmount = total;
            receipt.PaidAmount = Math.Clamp(request.PaidAmount, 0m, total);
            receipt.PaymentStatus = DeriveStatus(receipt.PaidAmount, total);

            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogService.LogActionAsync(
                AuditEntityTypes.ZakupReceipt, receipt.Id, AuditActions.Create, adminId,
                new { receipt.SupplierId, receipt.InvoiceNumber, ItemCount = request.Items.Count, receipt.TotalAmount, receipt.PaidAmount },
                cancellationToken);

            return await BuildReceiptDtoAsync(receipt.Id, marketId, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// «Отметить принятым» — «В пути» postavkani qabul qiladi: har liniya stoki
    /// omborga kiradi, tannarx yangilanadi, Приход harakati yoziladi. Faqat
    /// InTransit holatidan; Accepted bo'lsa xato.
    /// </summary>
    public async Task<ZakupReceiptDto?> AcceptZakupReceiptAsync(Guid receiptId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        return await _unitOfWork.ExecuteInTransactionAsync<ZakupReceiptDto?>(async () =>
        {
            var receipt = await _context.ZakupReceipts
                .FirstOrDefaultAsync(r => r.Id == receiptId && r.MarketId == marketId, cancellationToken);
            if (receipt is null)
                return null;
            if (receipt.DeliveryStatus == DeliveryStatus.Accepted)
                throw new InvalidOperationException("Postavka allaqachon qabul qilingan.");

            var lines = await _context.Zakups
                .Where(z => z.ReceiptId == receiptId && z.MarketId == marketId)
                .ToListAsync(cancellationToken);
            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await _context.Products
                .Where(p => productIds.Contains(p.Id) && p.MarketId == marketId)
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var line in lines)
                if (products.TryGetValue(line.ProductId, out var product))
                {
                    product.Quantity += line.Quantity;
                    product.CostPrice = line.CostPrice; // latest purchase price
                    _stockLedger.Record(product, line.Quantity, StockMovementType.Purchase,
                        refNumber: receipt.ReceiptNumber, userId: adminId);
                }

            receipt.DeliveryStatus = DeliveryStatus.Accepted;
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogService.LogActionAsync(
                AuditEntityTypes.ZakupReceipt, receipt.Id, AuditActions.Update, adminId,
                new { accepted = true, receipt.ReceiptNumber, ItemCount = lines.Count }, cancellationToken);

            return await BuildReceiptDtoAsync(receipt.Id, marketId, cancellationToken);
        }, cancellationToken);
    }

    // ── Receipt reads ─────────────────────────────────────────────────────────

    public async Task<IEnumerable<ZakupReceiptDto>> GetAllZakupReceiptsAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var receipts = await ReceiptQuery(marketId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        return receipts.Select(MapReceiptToDto).ToList();
    }

    public async Task<PagedResult<ZakupReceiptDto>> GetAllZakupReceiptsPagedAsync(int page, int size, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var marketId = _currentMarketService.GetCurrentMarketId();
        var baseQuery = ReceiptQuery(marketId);

        var total = await baseQuery.CountAsync(cancellationToken);
        var receipts = await baseQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return PagedResult<ZakupReceiptDto>.From(receipts.Select(MapReceiptToDto).ToList(), page, size, total);
    }

    public async Task<ZakupReceiptDto?> GetZakupReceiptByIdAsync(Guid receiptId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var receipt = await ReceiptQuery(marketId).FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
        return receipt is null ? null : MapReceiptToDto(receipt);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delete a single purchase line. NEVER blocked by downstream sales: the
    /// stock this line added is reversed but clamped at zero, and sales history
    /// is untouched because every SaleItem snapshots its own cost at sale time.
    /// The parent receipt's total/payment is adjusted, and an empty receipt is
    /// removed.
    /// </summary>
    public async Task<bool> DeleteZakupAsync(Guid id, Guid deletedByUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var zakup = await _context.Zakups
                .FirstOrDefaultAsync(z => z.Id == id && z.MarketId == marketId, cancellationToken);
            if (zakup is null)
                return false;

            // «В пути» liniya hech qachon stok qo'shmagan — o'chirishda uni
            // tiklamaymiz (aks holda qoldiq noto'g'ri kamayardi). Faqat qabul
            // qilingan (Accepted) postavka stoki qaytariladi.
            var accepted = !zakup.ReceiptId.HasValue || await _context.ZakupReceipts
                .Where(r => r.Id == zakup.ReceiptId.Value && r.MarketId == marketId)
                .Select(r => r.DeliveryStatus)
                .FirstOrDefaultAsync(cancellationToken) == DeliveryStatus.Accepted;

            // Single-line delete: sibling lines of its receipt survive, so they
            // remain valid cost-rollback candidates — pass null.
            if (accepted)
                await ReverseLineStockAsync(zakup, marketId, null, cancellationToken);

            if (zakup.ReceiptId.HasValue)
                await AdjustReceiptAfterLineRemovalAsync(zakup.ReceiptId.Value, zakup, marketId, cancellationToken);

            _context.Zakups.Remove(zakup);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogService.LogActionAsync(
                AuditEntityTypes.Zakup, id, AuditActions.Delete, deletedByUserId,
                new { zakup.ProductId, zakup.Quantity, zakup.CostPrice, zakup.ReceiptId }, cancellationToken);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Delete an entire goods-receipt and all its lines. Same guarantees as the
    /// single-line delete: each line's stock is reversed (clamped at zero),
    /// never blocked by sales, sales history untouched.
    /// </summary>
    public async Task<bool> DeleteZakupReceiptAsync(Guid receiptId, Guid deletedByUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var receipt = await _context.ZakupReceipts
                .FirstOrDefaultAsync(r => r.Id == receiptId && r.MarketId == marketId, cancellationToken);
            if (receipt is null)
                return false;

            var lines = await _context.Zakups
                .Where(z => z.ReceiptId == receiptId && z.MarketId == marketId)
                .ToListAsync(cancellationToken);

            // «В пути» postavka stok qo'shmagan — o'chirishda tiklanmaydi.
            var acceptedReceipt = receipt.DeliveryStatus == DeliveryStatus.Accepted;
            foreach (var line in lines)
            {
                // Exclude the whole receipt from cost-rollback — all its lines are
                // being deleted in this same (not-yet-flushed) transaction.
                if (acceptedReceipt)
                    await ReverseLineStockAsync(line, marketId, receiptId, cancellationToken);
                _context.Zakups.Remove(line);
            }

            _context.ZakupReceipts.Remove(receipt);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogService.LogActionAsync(
                AuditEntityTypes.ZakupReceipt, receiptId, AuditActions.Delete, deletedByUserId,
                new { receipt.SupplierId, receipt.InvoiceNumber, ItemCount = lines.Count, receipt.TotalAmount }, cancellationToken);

            return true;
        }, cancellationToken);
    }

    // ── Supplier payment ─────────────────────────────────────────────────────

    public async Task<ZakupReceiptDto?> RegisterSupplierPaymentAsync(Guid receiptId, decimal amount, Guid userId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            throw new InvalidOperationException("To'lov summasi 0 dan katta bo'lishi kerak");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync<ZakupReceiptDto?>(async () =>
        {
            var receipt = await _context.ZakupReceipts
                .FirstOrDefaultAsync(r => r.Id == receiptId && r.MarketId == marketId, cancellationToken);
            if (receipt is null)
                return null;

            receipt.PaidAmount = Math.Min(receipt.TotalAmount, receipt.PaidAmount + amount);
            receipt.PaymentStatus = DeriveStatus(receipt.PaidAmount, receipt.TotalAmount);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogService.LogActionAsync(
                AuditEntityTypes.ZakupReceipt, receiptId, AuditActions.Update, userId,
                new { Payment = amount, receipt.PaidAmount, receipt.TotalAmount }, cancellationToken);

            return await BuildReceiptDtoAsync(receiptId, marketId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<PurchaseSummaryDto> GetReceiptsSummaryAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var agg = await _context.ZakupReceipts
            .AsNoTracking()
            .Where(r => r.MarketId == marketId && r.CreatedAt >= fromUtc)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(r => r.TotalAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        return new PurchaseSummaryDto(agg?.Count ?? 0, agg?.Total ?? 0m);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static SupplierPaymentStatus DeriveStatus(decimal paid, decimal total)
    {
        if (paid <= 0) return SupplierPaymentStatus.Unpaid;
        if (paid >= total) return SupplierPaymentStatus.Paid;
        return SupplierPaymentStatus.Partial;
    }

    /// <summary>
    /// Reverse the stock a purchase line added, clamped at zero. Never throws on
    /// sold-out stock — that is the whole point (deletes must not be blocked by
    /// sales). Best-effort cost rollback: if the product's current cost equals
    /// this line's cost, fall back to the newest remaining purchase's cost.
    /// </summary>
    private async Task ReverseLineStockAsync(Zakup line, int marketId, Guid? excludeReceiptId, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == line.ProductId && p.MarketId == marketId, cancellationToken);
        if (product is null)
            return;

        var beforeQty = product.Quantity;
        product.Quantity = Math.Max(0m, product.Quantity - line.Quantity);
        // Xarid (receipt) o'chirildi — kelgan tovar qaytarib olindi. Delta =
        // haqiqiy o'zgarish (0 ga clamp bo'lishi mumkin, shuning uchun before−after).
        _stockLedger.Record(product, product.Quantity - beforeQty, StockMovementType.Correction,
            comment: "Xarid bekor qilindi");

        if (product.CostPrice == line.CostPrice)
        {
            // Roll cost back to the newest SURVIVING purchase. Exclude this line
            // and — when a whole receipt is being deleted — every sibling line of
            // that receipt too: they are marked Deleted but SaveChanges hasn't
            // flushed yet, so an unfiltered query would still return them and we
            // could roll cost onto a purchase that is itself about to vanish.
            var latestRemaining = await _context.Zakups
                .Where(z => z.ProductId == line.ProductId
                         && z.MarketId == marketId
                         && z.Id != line.Id
                         && (excludeReceiptId == null || z.ReceiptId != excludeReceiptId))
                .OrderByDescending(z => z.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestRemaining is not null)
                product.CostPrice = latestRemaining.CostPrice;
        }
    }

    private async Task AdjustReceiptAfterLineRemovalAsync(Guid receiptId, Zakup removedLine, int marketId, CancellationToken cancellationToken)
    {
        var receipt = await _context.ZakupReceipts
            .FirstOrDefaultAsync(r => r.Id == receiptId && r.MarketId == marketId, cancellationToken);
        if (receipt is null)
            return;

        var remaining = await _context.Zakups
            .CountAsync(z => z.ReceiptId == receiptId && z.MarketId == marketId && z.Id != removedLine.Id, cancellationToken);

        if (remaining == 0)
        {
            _context.ZakupReceipts.Remove(receipt);
            return;
        }

        receipt.TotalAmount = Math.Max(0m, receipt.TotalAmount - removedLine.Quantity * removedLine.CostPrice);
        receipt.PaidAmount = Math.Min(receipt.PaidAmount, receipt.TotalAmount);
        receipt.PaymentStatus = DeriveStatus(receipt.PaidAmount, receipt.TotalAmount);
    }

    // History display must show the supplier/product name even after either has
    // been soft-deleted — IgnoreQueryFilters keeps those rows in the Include.
    private IQueryable<ZakupReceipt> ReceiptQuery(int marketId) =>
        _context.ZakupReceipts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(r => r.Supplier)
            .Include(r => r.CreatedByAdmin)
            .Include(r => r.Items.OrderBy(i => i.CreatedAt))
                .ThenInclude(i => i.Product)
            .Where(r => r.MarketId == marketId);

    private async Task<ZakupReceiptDto> BuildReceiptDtoAsync(Guid receiptId, int marketId, CancellationToken cancellationToken)
    {
        var receipt = await ReceiptQuery(marketId).FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
        if (receipt is null)
            throw new InvalidOperationException("Priyomka topilmadi");
        return MapReceiptToDto(receipt);
    }

    private static ZakupReceiptDto MapReceiptToDto(ZakupReceipt r) => new(
        r.Id,
        r.ReceiptNumber,
        r.SupplierId,
        r.Supplier?.Name,
        r.InvoiceNumber,
        r.TotalAmount,
        r.PaidAmount,
        r.TotalAmount - r.PaidAmount,
        r.PaymentStatus.ToString(),
        r.DeliveryStatus.ToString(),
        r.Comment,
        r.Items.Count,
        r.CreatedAt,
        r.CreatedByAdmin?.FullName ?? "Unknown",
        r.Items.Select(i => new ZakupReceiptLineDto(
            i.Id,
            i.ProductId,
            i.Product?.Name ?? "Unknown",
            i.Quantity,
            i.CostPrice,
            i.Quantity * i.CostPrice)).ToList()
    );

    private static ZakupDto MapToDtoEager(Zakup zakup) => new(
        zakup.Id,
        zakup.ProductId,
        zakup.Product?.Name ?? "Unknown",
        zakup.Quantity,
        zakup.CostPrice,
        zakup.CreatedAt,
        zakup.CreatedByAdmin?.FullName ?? "Unknown"
    );

    private async Task<ZakupDto> MapToDtoAsync(Zakup zakup, CancellationToken cancellationToken)
    {
        // Get product and verify it belongs to the same market as the zakup
        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == zakup.ProductId && p.MarketId == zakup.MarketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        // Get admin - admin should be from the same market
        var admins = await _unitOfWork.Users.FindAsync(
            u => u.Id == zakup.CreatedByAdminId && u.MarketId == zakup.MarketId,
            cancellationToken);
        var admin = admins.FirstOrDefault();

        return new ZakupDto(
            zakup.Id,
            zakup.ProductId,
            product?.Name ?? "Unknown",
            zakup.Quantity,
            zakup.CostPrice,
            zakup.CreatedAt,
            admin?.FullName ?? "Unknown"
        );
    }
}
