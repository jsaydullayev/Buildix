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
/// Reversal concern extracted from SaleService: cancel a sale, delete a sale,
/// return a line. All return stock to inventory and reconcile cash / debt.
/// See <see cref="ISaleReversalService"/>. Total recompute is delegated to
/// <see cref="SaleTotals"/>.
/// </summary>
public class SaleReversalService : ISaleReversalService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SaleReversalService> _logger;
    private readonly IStockLedger _stockLedger;

    public SaleReversalService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService, IAuditLogService auditLogService, ILogger<SaleReversalService> logger, IStockLedger stockLedger)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _auditLogService = auditLogService;
        _logger = logger;
        _stockLedger = stockLedger;
    }

    public async Task<Result<SaleDto>> CancelSaleAsync(Guid saleId, Guid adminId, CancellationToken cancellationToken = default)
    {
        // adminId is the JWT-extracted caller identity (controller pulls it
        // from ClaimTypes.NameIdentifier). It used to be a string parsed
        // from a client-supplied request body, which let any caller with
        // sales.delete forge another admin's id into the audit row.
        _logger.LogInformation("CancelSale by Admin {AdminId}", adminId);

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var marketId = _currentMarketService.GetCurrentMarketId();

            // Serialise against a concurrent payment on this sale (money race).
            await LockSaleForUpdateAsync(saleId, cancellationToken);

            var sales = await _unitOfWork.Sales.FindAsync(
                s => s.Id == saleId && s.MarketId == marketId,
                cancellationToken);
            var sale = sales.FirstOrDefault();

            if (sale is null)
            {
                _logger.LogWarning("Sale not found: {SaleId}", saleId);
                return Result.Failure<SaleDto>("Sale not found", "NOT_FOUND");
            }

            if (sale.Status == SaleStatus.Cancelled)
                return Result.Failure<SaleDto>("Sale is already cancelled");

            // Ombor jurnali: SaleReversal faqat YAKUNLANGAN sotuv bekor qilinganda
            // yoziladi. Draft'da Продажа harakati umuman yozilmagan edi, shuning
            // uchun uni "qaytarish" ham yozilmaydi (net-nol, jurnal toza qoladi).
            var wasFinalized = sale.Status != SaleStatus.Draft;

            // Restore stock for all items.
            // P4 — fetch every affected Product in ONE round trip instead of
            // one-per-item. A cancelled sale with 50 ordinary items used to
            // fire 50 separate `Products WHERE Id = ?` queries; now we issue
            // a single `Products WHERE Id IN (...)`. External items have no
            // ProductId so they're filtered out upfront.
            var saleItems = (await _unitOfWork.SaleItems.FindAsync(
                si => si.SaleId == saleId, cancellationToken)).ToList();

            var ordinaryProductIds = saleItems
                .Where(i => !i.IsExternal && i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct()
                .ToList();

            if (ordinaryProductIds.Count > 0)
            {
                var products = await _context.Products
                    .Where(p => ordinaryProductIds.Contains(p.Id) && p.MarketId == marketId)
                    .ToDictionaryAsync(p => p.Id, cancellationToken);

                foreach (var item in saleItems)
                {
                    if (item.IsExternal || !item.ProductId.HasValue) continue;
                    if (products.TryGetValue(item.ProductId.Value, out var product))
                    {
                        if (product.IsTemporary)
                        {
                            // Vaqtinchalik mahsulot: bekor qilingan sotuvda yaratilgan,
                            // inventarda qolmasin — soft-delete qilamiz.
                            product.IsDeleted = true;
                            product.DeletedAt = DateTime.UtcNow;
                        }
                        else
                        {
                            product.Quantity += item.Quantity;
                            if (wasFinalized)
                                _stockLedger.Record(product, item.Quantity, StockMovementType.SaleReversal,
                                    refNumber: sale.SaleNumber, userId: adminId, comment: "Sotuv bekor qilindi");
                        }
                        _unitOfWork.Products.Update(product);
                    }
                }
            }
            // External items (IsExternal == true) have no stock to restore.

            // Refund cash payments back to the till. Card / Click / Terminal payments
            // flow through external rails (POS / payment processor) so they don't touch
            // our CashRegister — only Cash payments must be reversed here. The Payment
            // records themselves stay in place as an audit trail.
            var cashPayments = await _unitOfWork.Payments.FindAsync(
                p => p.SaleId == saleId && p.PaymentType == PaymentType.Cash,
                cancellationToken);
            var cashRefund = cashPayments.Sum(p => p.Amount);
            if (cashRefund > 0)
            {
                var cashRegister = await _context.CashRegisters
                    .FirstOrDefaultAsync(cr => cr.MarketId == sale.MarketId, cancellationToken);
                if (cashRegister != null)
                {
                    cashRegister.CurrentBalance -= cashRefund;
                    cashRegister.LastUpdated = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Sale {SaleId} cancelled — refunded {Amount} cash to market {MarketId} till",
                        saleId, cashRefund, sale.MarketId);
                    if (cashRegister.CurrentBalance < 0)
                        _logger.LogWarning(
                            "CashRegister balance went negative after cancelling sale {SaleId}: Balance={Balance}. Manual reconciliation may be needed.",
                            saleId, cashRegister.CurrentBalance);
                }
            }

            // Update sale status
            sale.Status = SaleStatus.Cancelled;
            _unitOfWork.Sales.Update(sale);

            // S4 — close the associated debt cleanly. The previous code
            // relied on `sale.Debt` being eagerly loaded; the query above
            // does NOT include it, so `sale.Debt` was always null and the
            // debt never closed when the sale was cancelled. The customer's
            // total outstanding balance kept showing the cancelled sale's
            // RemainingDebt — a real financial-correctness bug. Look the
            // debt up directly, mark it Closed AND zero RemainingDebt so
            // the customer's running total stays consistent.
            var openDebts = await _unitOfWork.Debts.FindAsync(
                d => d.SaleId == saleId && d.Status == DebtStatus.Open,
                cancellationToken);
            foreach (var debt in openDebts)
            {
                debt.Status = DebtStatus.Closed;
                debt.RemainingDebt = 0;
                _unitOfWork.Debts.Update(debt);
            }

            // P6 — stage the audit row on the same DbContext BEFORE the
            // single SaveChanges so business state + audit INSERT batch into
            // one round trip instead of two. The audit row will now commit /
            // rollback with the surrounding business transaction (which is
            // the stronger guarantee here — if the cancel rolls back we
            // don't want a "Cancel" audit row lingering).
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, saleId, AuditActions.Cancel, adminId,
                new
                {
                    SaleId = saleId,
                    sale.SellerId,
                    sale.CustomerId,
                    Status = sale.Status.ToString(),
                    sale.TotalAmount,
                    sale.PaidAmount,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken));
        }, cancellationToken);
    }

    public async Task<Result<SaleDto>> DeleteSaleAsync(Guid saleId, Guid userId, Guid? requireOwnDraftOf = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Serialise against a concurrent payment on this sale (money race).
            await LockSaleForUpdateAsync(saleId, cancellationToken);

            var sale = await _context.Sales
                .Include(s => s.SaleItems)
                    .ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(s => s.Id == saleId && s.MarketId == marketId, cancellationToken);

            if (sale is null)
            {
                _logger.LogWarning("Sale not found: {SaleId} in MarketId: {MarketId}", saleId, marketId);
                return Result.Failure<SaleDto>("Sale not found", "NOT_FOUND");
            }

            if (sale.IsDeleted)
            {
                _logger.LogWarning("Sale already deleted: {SaleId}", saleId);
                return Result.Failure<SaleDto>("Sale already deleted", "NOT_FOUND");
            }

            // Restricted delete: a cashier without sales.delete may discard the
            // parked receipt they themselves built, and nothing else. Checked
            // INSIDE the transaction, after the row lock — a status read taken
            // before the lock could be stale by the time the delete lands, which
            // on this path would mean deleting a paid sale without the permission.
            if (requireOwnDraftOf.HasValue &&
                (sale.Status != SaleStatus.Draft || sale.SellerId != requireOwnDraftOf.Value))
            {
                _logger.LogWarning("Own-draft delete refused: {SaleId}, Status: {Status}, Seller: {SellerId}, Caller: {Caller}",
                    saleId, sale.Status, sale.SellerId, requireOwnDraftOf.Value);
                return Result.Failure<SaleDto>("Faqat o'zingiz yaratgan yakunlanmagan chekni o'chira olasiz.", "FORBIDDEN");
            }

            if (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Paid)
            {
                _logger.LogWarning("Sale cannot be deleted: {SaleId}, Status: {Status}", saleId, sale.Status);
                return Result.Failure<SaleDto>("Faqat draft yoki to'langan (Paid) savdolarini o'chirish mumkin! Qarzli savdolarni o'chirib bo'lmaydi.");
            }

            // SaleReversal faqat YAKUNLANGAN (Paid) sotuv o'chirilganda yoziladi.
            // Draft o'chirish — Продажа umuman qayd etilmagani uchun, jurnalga
            // hech narsa yozilmaydi (net-nol).
            var wasFinalized = sale.Status != SaleStatus.Draft;

            // Save sale items for DTO
            var saleItems = sale.SaleItems.ToList();
            _logger.LogInformation("Found {Count} sale items to delete", saleItems.Count);

            foreach (var saleItem in saleItems)
            {
                _logger.LogInformation("Deleting SaleItem: {SaleItemId}, Product: {ProductId}, Qty: {Quantity}, IsExternal: {IsExternal}",
                    saleItem.Id, saleItem.ProductId, saleItem.Quantity, saleItem.IsExternal);

                // ============================================
                // ✅ ISEXTERNAL SHARTI - STOKNI QAYTARISH
                // ============================================
                if (!saleItem.IsExternal && saleItem.Product != null)
                {
                    if (saleItem.Product.IsTemporary)
                    {
                        // Vaqtinchalik mahsulot faqat shu savdo uchun yaratilgan —
                        // stokni qaytarish o'rniga soft-delete qilamiz (aynan
                        // CancelSaleAsync kabi), aks holda inventarda fantom
                        // mahsulot va soxta qoldiq qolib ketardi.
                        saleItem.Product.IsDeleted = true;
                        saleItem.Product.DeletedAt = DateTime.UtcNow;
                        _logger.LogInformation("Temporary product soft-deleted on sale delete: {ProductId}",
                            saleItem.ProductId);
                    }
                    else
                    {
                        // Oddiy mahsulot uchun stokni qaytarish
                        saleItem.Product.Quantity += saleItem.Quantity;
                        if (wasFinalized)
                            _stockLedger.Record(saleItem.Product, saleItem.Quantity, StockMovementType.SaleReversal,
                                refNumber: sale.SaleNumber, userId: userId, comment: "Sotuv o'chirildi");
                        _logger.LogInformation("Product stock restored: {ProductId}, Qty: +{Quantity}",
                            saleItem.ProductId, saleItem.Quantity);
                    }
                    _unitOfWork.Products.Update(saleItem.Product);
                }
                // Tashqi mahsulotlar - stokni o'zgarmaslik
            }

            // Savdoni o'chirish (soft delete - IsDeleted = true)
            sale.IsDeleted = true;
            sale.DeletedAt = DateTime.UtcNow;
            _context.Sales.Update(sale);
            _logger.LogInformation("Sale marked as deleted: {SaleId}", saleId);

            // Payments ham o'chirilishi kerak
            var payments = await _context.Payments
                .Where(p => p.SaleId == saleId)
                .ToListAsync(cancellationToken);

            // BEFORE deleting the payments, reverse the cash side. Otherwise a
            // Paid sale whose `CurrentBalance += amount` already landed in the
            // market's cash register would leave that money in the register
            // forever — effectively making the customer's cash disappear.
            // Card / Transfer / Click / Credit payments don't touch the
            // register so we only reverse Cash.
            //
            // netCashOnSale = positive cash payments + negative cash refunds
            //   > 0 : sale brought net cash in — back it out of the register
            //   < 0 : sale net-refunded the customer (overpaid/return) — the
            //         register was previously debited by `|net|`; add it back
            //   = 0 : nothing to do
            var netCashOnSale = payments
                .Where(p => p.PaymentType == PaymentType.Cash)
                .Sum(p => p.Amount);
            if (netCashOnSale != 0)
            {
                var cashRegister = await _context.CashRegisters
                    .FirstOrDefaultAsync(cr => cr.MarketId == sale.MarketId, cancellationToken);
                if (cashRegister != null)
                {
                    cashRegister.CurrentBalance -= netCashOnSale;
                    cashRegister.LastUpdated = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Cash reversed on sale delete: SaleId={SaleId} NetCash={Amount} NewBalance={Balance}",
                        saleId, netCashOnSale, cashRegister.CurrentBalance);
                    if (cashRegister.CurrentBalance < 0)
                        _logger.LogWarning(
                            "CashRegister balance went negative after deleting sale {SaleId}: Balance={Balance}. Manual reconciliation may be needed.",
                            saleId, cashRegister.CurrentBalance);
                }
                else
                {
                    _logger.LogWarning(
                        "No CashRegister for MarketId={MarketId} when reversing sale {SaleId} (net {Amount} cash). " +
                        "Skipping reversal — this should not happen in a normally-seeded market.",
                        sale.MarketId, saleId, netCashOnSale);
                }
            }

            foreach (var payment in payments)
            {
                _context.Payments.Remove(payment);
                _logger.LogInformation("Payment deleted: {PaymentId}", payment.Id);
            }

            // Fraud audit: a deleted sale reverses stock AND cash — record who
            // did it. Staged on the same DbContext so the audit row commits /
            // rolls back with the delete. Feeds the bulk-delete burst detector.
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, saleId, AuditActions.Delete, userId,
                new
                {
                    SaleId = saleId,
                    sale.SaleNumber,
                    sale.SellerId,
                    sale.CustomerId,
                    PreviousStatus = sale.Status.ToString(),
                    sale.TotalAmount,
                    sale.PaidAmount,
                    ReversedCash = netCashOnSale,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // DTO ni yaratish
            var itemsDto = new List<SaleItemDto>();
            foreach (var si in saleItems)
            {
                string productName;
                string unit = "";
                if (!si.IsExternal)
                {
                    productName = si.Product?.Name ?? "Unknown";
                    unit = si.Product?.GetUnitName() ?? "";
                }
                else
                {
                    productName = si.ExternalProductName ?? "Tashqi mahsulot";
                }
                itemsDto.Add(new SaleItemDto(
                    si.Id.ToString(),
                    si.SaleId.ToString(),
                    si.ProductId,
                    productName,
                    si.Quantity,
                    si.IsExternal ? si.ExternalCostPrice : si.CostPrice,
                    si.SalePrice,
                    si.TotalPrice,
                    (si.SalePrice - (si.IsExternal ? si.ExternalCostPrice : si.CostPrice)) * si.Quantity,
                    unit,
                    si.Comment,
                    si.IsExternal
                ));
            }

            var paymentsDto = payments.Select(p => new PaymentDto(
                p.Id,
                p.PaymentType.ToString(),
                p.Amount,
                p.CreatedAt,
                null,
                null,
                null
            )).ToList();

            var seller = await _unitOfWork.Users.GetByIdAsync(sale.SellerId, cancellationToken);
            var customer = sale.CustomerId.HasValue ? await _unitOfWork.Customers.GetByIdAsync(sale.CustomerId.Value, cancellationToken) : null;

            return Result.Success(new SaleDto(
                sale.Id,
                sale.SaleNumber,
                sale.SellerId,
                seller?.FullName ?? "Unknown",
                sale.CustomerId,
                customer?.FullName,
                customer?.Phone,
                sale.Status.ToString(),
                sale.TotalAmount,
                sale.PaidAmount,
                sale.TotalAmount - sale.PaidAmount,
                sale.DiscountAmount,
                sale.CreatedAt,
                itemsDto,
                paymentsDto
            ));
        }, cancellationToken);
    }

    public async Task<SaleItemDto?> ReturnSaleItemAsync(Guid saleId, ReturnSaleItemRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync<SaleItemDto?>(async () =>
        {
            // Serialise against a concurrent payment on this sale (money race):
            // a return adjusts PaidAmount + the till, exactly like a payment.
            await LockSaleForUpdateAsync(saleId, cancellationToken);

            var sale = await _context.Sales
                .Include(s => s.SaleItems)
                    .ThenInclude(si => si.Product)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == saleId && s.MarketId == marketId, cancellationToken);

            if (sale == null)
                return null;

            var saleItem = sale.SaleItems.FirstOrDefault(si => si.Id.ToString() == request.SaleItemId);
            if (saleItem == null)
                return null;

            if (request.Quantity <= 0 || request.Quantity > saleItem.Quantity)
                return null;

            var returnQuantity = request.Quantity;
            var refundAmount = returnQuantity * saleItem.SalePrice;

            // Update sale item quantity or remove
            string originalComment = saleItem.Comment ?? "";
            var isFullReturn = returnQuantity >= saleItem.Quantity;

            if (isFullReturn)
            {
                _unitOfWork.SaleItems.Delete(saleItem);
            }
            else
            {
                saleItem.Quantity -= returnQuantity;

                var returnComment = !string.IsNullOrEmpty(request.Comment)
                    ? request.Comment
                    : $"Qaytarildi: {returnQuantity} ({DateTime.UtcNow:dd.MM.yyyy HH:mm})";
                saleItem.Comment = !string.IsNullOrEmpty(originalComment)
                    ? $"{originalComment} | {returnComment}"
                    : returnComment;

                _unitOfWork.SaleItems.Update(saleItem);
            }

            // Restore stock for ordinary products only
            if (!saleItem.IsExternal && saleItem.Product != null)
            {
                saleItem.Product.Quantity += returnQuantity;
                // Qaytarish har doim yakunlangan (Paid) sotuvda bo'ladi — SaleReversal
                // harakati (qisman miqdor) jurnalga tushadi.
                _stockLedger.Record(saleItem.Product, returnQuantity, StockMovementType.SaleReversal,
                    refNumber: sale.SaleNumber, userId: userId, comment: "Qaytarish");
                _context.Products.Update(saleItem.Product);
            }

            // Save the SaleItem deletion/update so SUM-from-items sees the
            // post-return state, then recompute Sale.TotalAmount as the
            // authoritative SUM. Replaces the old `-= refundAmount`
            // arithmetic with the same drift-resistant pattern used by
            // Add/Remove SaleItem.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await RecalculateSaleTotalAsync(sale, cancellationToken);

            // Adjust paid amount if overpaid
            if (sale.PaidAmount > sale.TotalAmount)
            {
                var overpaid = sale.PaidAmount - sale.TotalAmount;
                sale.PaidAmount = sale.TotalAmount;

                // Use the same payment type the customer actually paid with.
                // Hardcoding Cash here would (a) lie in the audit trail when the
                // original was Terminal/Click/Transfer, and (b) cause the cash
                // register deduction below to debit money that was never in it.
                // Pick the type that dominates the positive payments on this sale.
                var dominantType = sale.Payments
                    .Where(p => p.Amount > 0)
                    .GroupBy(p => p.PaymentType)
                    .OrderByDescending(g => g.Sum(p => p.Amount))
                    .Select(g => (PaymentType?)g.Key)
                    .FirstOrDefault() ?? PaymentType.Cash;

                var refundPayment = new Payment
                {
                    Id = Guid.NewGuid(),
                    SaleId = sale.Id,
                    PaymentType = dominantType,
                    Amount = -overpaid,
                    MarketId = marketId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Payments.Add(refundPayment);

                // ONLY touch the cash register when the original payment was Cash.
                // Terminal/Click/Transfer/Credit refunds happen out-of-band (bank
                // reversal, platform refund) and never move physical till money.
                if (dominantType == PaymentType.Cash)
                {
                    var cashRegister = await _context.CashRegisters
                        .FirstOrDefaultAsync(cr => cr.MarketId == marketId, cancellationToken);
                    if (cashRegister != null)
                    {
                        cashRegister.CurrentBalance -= overpaid;
                        cashRegister.LastUpdated = DateTime.UtcNow;
                        _logger.LogInformation(
                            "Cash refunded on item return: SaleId={SaleId} Amount={Amount} NewBalance={Balance}",
                            sale.Id, overpaid, cashRegister.CurrentBalance);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No CashRegister for MarketId={MarketId} during return of sale {SaleId}; refund record kept but balance unchanged.",
                            marketId, sale.Id);
                    }
                }
            }

            // Sync the Debt record if one exists. Without this, returning items
            // from a debt-sale left the customer's debt frozen at the original
            // amount even though they actually owe less now (e.g. 100k debt
            // with 50k paid; return 20k of items → debt is now 30k, not 50k).
            var debt = (await _unitOfWork.Debts.FindAsync(
                d => d.SaleId == saleId && d.MarketId == marketId,
                cancellationToken)).FirstOrDefault();
            if (debt != null && debt.Status == DebtStatus.Open)
            {
                // Source of truth = sale.TotalAmount and PaidAmount (already
                // updated above). RemainingDebt = TotalAmount - PaidAmount.
                var newRemaining = Math.Max(0m, sale.TotalAmount - sale.PaidAmount);
                debt.RemainingDebt = newRemaining;
                // Reduce TotalDebt proportionally so reports show the
                // adjusted debt amount, not the historic original.
                debt.TotalDebt = Math.Max(0m, debt.TotalDebt - refundAmount);
                if (newRemaining <= 0)
                {
                    debt.Status = DebtStatus.Closed;
                    _logger.LogInformation(
                        "Debt auto-closed by return: SaleId={SaleId} (full return covered remaining debt)",
                        sale.Id);
                }
                _unitOfWork.Debts.Update(debt);
            }

            // Oxirgi tovar qaytarilib, savdoda hech qanday mahsulot qolmasa —
            // savdo bekor qilinadi (Cancelled). Barcha tovar qaytarilgan =
            // savdo amalda yo'q. Cancelled hisobotlardan chiqarib tashlanadi
            // (filtr: != Draft && != Cancelled) — aks holda bo'sh (0 summa)
            // savdo "savdolar soni"ni oshirib, o'rtachani buzardi.
            if (isFullReturn)
            {
                var remainingItems = await _context.SaleItems
                    .CountAsync(si => si.SaleId == saleId, cancellationToken);
                if (remainingItems == 0)
                {
                    sale.Status = SaleStatus.Cancelled;
                    _unitOfWork.Sales.Update(sale);
                    _logger.LogInformation(
                        "Sale {SaleId} cancelled: all items returned, none remain.",
                        sale.Id);
                }
            }

            // Fraud audit: a return refunds money and returns stock — the
            // classic "process a bogus return, pocket the cash" vector. Record
            // the actor + amount on the same transaction as the return itself.
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, saleId, AuditActions.Return, userId,
                new
                {
                    SaleId = saleId,
                    request.SaleItemId,
                    ReturnQuantity = returnQuantity,
                    RefundAmount = refundAmount,
                    IsFullReturn = isFullReturn,
                    sale.TotalAmount,
                    sale.PaidAmount,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (!isFullReturn && saleItem != null)
            {
                string productName;
                string unit = "";

                if (!saleItem.IsExternal)
                {
                    productName = saleItem.Product?.Name ?? "Unknown";
                    unit = saleItem.Product?.GetUnitName() ?? "";
                }
                else
                {
                    productName = saleItem.ExternalProductName ?? "Unknown";
                    unit = "";
                }

                return new SaleItemDto(
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
                );
            }

            return null;
        }, cancellationToken);
    }

    private async Task RecalculateSaleTotalAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        // Shared charged-total formula (SUM of items − discount, clamped at 0).
        await SaleTotals.RecalculateAsync(_context, sale, cancellationToken);
        _unitOfWork.Sales.Update(sale);
    }

    /// <summary>
    /// Lock the Sale row FOR UPDATE for the rest of the current transaction.
    /// Cancel / delete / return all do a read-modify-write on sale.PaidAmount
    /// AND debit/credit the shared CashRegister, yet Sale carries NO xmin
    /// concurrency token (disabled by design — see AppDbContext). Without this
    /// lock a concurrent AddPayment (which DOES lock, SalePaymentService) racing
    /// a return/cancel could both read the same PaidAmount and last-write-wins,
    /// losing a payment and double-adjusting the till. Taking the same lock here
    /// serialises them. FOR UPDATE is PostgreSQL-only; the InMemory test provider
    /// skips it. Must be "SELECT *, xmin": Sale maps xmin to PostgreSQL's system
    /// column and PG's `*` never expands it, so omitting it raises 42703.
    /// </summary>
    private async Task LockSaleForUpdateAsync(Guid saleId, CancellationToken cancellationToken)
    {
        if (_context.Database.ProviderName?.Contains("InMemory") == false)
        {
            await _context.Sales
                .FromSqlInterpolated($"SELECT *, xmin FROM \"Sales\" WHERE \"Id\" = {saleId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
