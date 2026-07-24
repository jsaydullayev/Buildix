using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Возвраты» — first-class qaytarish hujjatlari (В-##). Bitta sotuvdan bir necha
/// tovar liniyasini bitta hujjatda qaytaradi: stok qaytariladi (+SaleReversal
/// harakati), savdo summasi qayta hisoblanadi, ortiqcha to'lov tanlangan usulda
/// qaytariladi (naqd bo'lsa kassadan + Касса jurnaliga), sabab/usul saqlanadi.
/// Barchasi bitta tranzaksiyada.
/// </summary>
public class SaleReturnService : ISaleReturnService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IAuditLogService _auditLog;
    private readonly IStockLedger _stockLedger;
    private readonly ICashLedger _cashLedger;

    public SaleReturnService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService,
        IAuditLogService auditLog, IStockLedger stockLedger, ICashLedger cashLedger)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _auditLog = auditLog;
        _stockLedger = stockLedger;
        _cashLedger = cashLedger;
    }

    public async Task<Result<SaleReturnDto>> CreateReturnAsync(CreateReturnDto request, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ReturnReason>(request.Reason, ignoreCase: true, out var reason))
            return Result.Failure<SaleReturnDto>("Noto'g'ri qaytarish sababi.");
        if (!Enum.TryParse<PaymentType>(request.RefundMethod, ignoreCase: true, out var refundMethod))
            return Result.Failure<SaleReturnDto>("Noto'g'ri pul qaytarish usuli.");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Serialise against a concurrent payment/return on this sale (money race).
            if (_context.Database.ProviderName?.Contains("InMemory") == false)
            {
                await _context.Sales
                    .FromSqlInterpolated($"SELECT *, xmin FROM \"Sales\" WHERE \"Id\" = {request.SaleId} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var sale = await _context.Sales
                .Include(s => s.SaleItems).ThenInclude(si => si.Product)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == request.SaleId && s.MarketId == marketId, cancellationToken);
            if (sale is null)
                return Result.Failure<SaleReturnDto>("Sotuv topilmadi.", "NOT_FOUND");

            // Qaytarish faqat YAKUNLANGAN sotuvda (Draft/Cancelled emas).
            if (sale.Status == SaleStatus.Draft || sale.Status == SaleStatus.Cancelled)
                return Result.Failure<SaleReturnDto>("Faqat yakunlangan sotuvdan qaytarish mumkin.");

            var returnItems = new List<SaleReturnItem>();
            decimal returnTotal = 0m;

            foreach (var line in request.Items)
            {
                var saleItem = sale.SaleItems.FirstOrDefault(si => si.Id == line.SaleItemId);
                if (saleItem is null)
                    return Result.Failure<SaleReturnDto>("Sotuv liniyasi topilmadi.");
                if (line.Quantity <= 0 || line.Quantity > saleItem.Quantity)
                    return Result.Failure<SaleReturnDto>($"Qaytarish miqdori noto'g'ri (mavjud: {saleItem.Quantity}).");

                var productName = saleItem.IsExternal
                    ? (saleItem.ExternalProductName ?? "Tashqi mahsulot")
                    : (saleItem.Product?.Name ?? "Noma'lum");

                returnItems.Add(new SaleReturnItem
                {
                    Id = Guid.NewGuid(),
                    SaleItemId = saleItem.Id,
                    ProductId = saleItem.ProductId,
                    ProductName = productName,
                    Quantity = line.Quantity,
                    UnitPrice = saleItem.SalePrice,
                });
                returnTotal += line.Quantity * saleItem.SalePrice;

                // Stokni qaytarish (tashqi liniyalarda stok yo'q).
                if (!saleItem.IsExternal && saleItem.Product != null)
                {
                    saleItem.Product.Quantity += line.Quantity;
                    _stockLedger.Record(saleItem.Product, line.Quantity, StockMovementType.SaleReversal,
                        refNumber: sale.SaleNumber, userId: userId, comment: "Возврат");
                    _context.Products.Update(saleItem.Product);
                }

                // Liniyani kamaytirish yoki o'chirish.
                if (line.Quantity >= saleItem.Quantity)
                    _unitOfWork.SaleItems.Delete(saleItem);
                else
                {
                    saleItem.Quantity -= line.Quantity;
                    _unitOfWork.SaleItems.Update(saleItem);
                }
            }

            // Yangi savdo summasi (SUM-from-items). Item o'zgarishlari ko'rinishi uchun avval saqlaymiz.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await SaleTotals.RecalculateAsync(_context, sale, cancellationToken);

            // Ortiqcha to'lov — mijozga qaytariladigan haqiqiy pul. Qarzli (to'liq
            // to'lanmagan) sotuvda qaytarish avval qarzni kamaytiradi, keyin ortig'i
            // qaytariladi — mavjud ReturnSaleItem semantikasi bilan bir xil.
            if (sale.PaidAmount > sale.TotalAmount)
            {
                var refund = sale.PaidAmount - sale.TotalAmount;
                sale.PaidAmount = sale.TotalAmount;

                _context.Payments.Add(new Payment
                {
                    Id = Guid.NewGuid(),
                    SaleId = sale.Id,
                    PaymentType = refundMethod,
                    Amount = -refund,
                    MarketId = marketId,
                    CollectedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                });

                // Naqd qaytarish faqat kassadan chiqadi (Terminal/Transfer tashqi
                // rельslarда — bank/platforma qaytaradi, kassaga tegmaydi).
                if (refundMethod == PaymentType.Cash)
                {
                    var register = await _context.CashRegisters
                        .FirstOrDefaultAsync(cr => cr.MarketId == marketId, cancellationToken);
                    if (register != null)
                    {
                        register.CurrentBalance -= refund;
                        register.LastUpdated = DateTime.UtcNow;
                    }
                    _cashLedger.Record(marketId, -refund, CashMovementType.Expense,
                        userId: userId, refNumber: sale.SaleNumber, comment: "Возврат");
                }
            }

            // Hujjat raqami (В-##) — per-market advisory lock bilan.
            await MarketSequenceLock.AcquireAsync(_context, MarketSequenceLock.SaleReturnNumberClass, marketId, cancellationToken);
            var nextNumber = (await _context.SaleReturns
                .Where(r => r.MarketId == marketId)
                .MaxAsync(r => (int?)r.Number, cancellationToken) ?? 0) + 1;

            var saleReturn = new SaleReturn
            {
                Id = Guid.NewGuid(),
                MarketId = marketId,
                Number = nextNumber,
                SaleId = sale.Id,
                Reason = reason,
                RefundMethod = refundMethod,
                TotalAmount = returnTotal,
                Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                Items = returnItems,
            };
            _context.SaleReturns.Add(saleReturn);

            _unitOfWork.Sales.Update(sale);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _auditLog.LogActionAsync(
                AuditEntityTypes.Sale, sale.Id, AuditActions.Return, userId,
                new { ReturnNumber = nextNumber, sale.SaleNumber, reason = reason.ToString(), refundMethod = refundMethod.ToString(), returnTotal },
                cancellationToken);

            return Result.Success(await BuildDtoAsync(saleReturn.Id, marketId, cancellationToken));
        }, cancellationToken);
    }

    public async Task<PagedResult<SaleReturnDto>> GetReturnsPagedAsync(int page, int size, string? reason = null, string? search = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);
        var marketId = _currentMarketService.GetCurrentMarketId();

        var query = _context.SaleReturns
            .AsNoTracking()
            .Include(r => r.Sale)
            .Include(r => r.User)
            .Include(r => r.Items)
            .Where(r => r.MarketId == marketId);

        if (!string.IsNullOrWhiteSpace(reason) && Enum.TryParse<ReturnReason>(reason, true, out var rr))
            query = query.Where(r => r.Reason == rr);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (int.TryParse(term.TrimStart('В', 'B', 'Ч', 'C', '-', '№', ' '), out var num))
                query = query.Where(r => r.Number == num || (r.Sale != null && r.Sale.SaleNumber == num));
            else
            {
                var lower = term.ToLower();
                query = query.Where(r => r.Items.Any(i => i.ProductName.ToLower().Contains(lower)));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return PagedResult<SaleReturnDto>.From(rows.Select(MapToDto).ToList(), page, size, total);
    }

    public async Task<ReturnsSummaryDto> GetReturnsSummaryAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var agg = await _context.SaleReturns
            .AsNoTracking()
            .Where(r => r.MarketId == marketId && r.CreatedAt >= fromUtc)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(r => r.TotalAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        var revenue = await _context.Sales
            .AsNoTracking()
            .Where(s => s.MarketId == marketId && s.CreatedAt >= fromUtc
                && s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled)
            .SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;

        var returnTotal = agg?.Total ?? 0m;
        var pct = revenue > 0 ? Math.Round(returnTotal / revenue * 100m, 1) : 0m;
        return new ReturnsSummaryDto(agg?.Count ?? 0, returnTotal, pct);
    }

    private async Task<SaleReturnDto> BuildDtoAsync(Guid id, int marketId, CancellationToken cancellationToken)
    {
        var r = await _context.SaleReturns
            .AsNoTracking()
            .Include(x => x.Sale)
            .Include(x => x.User)
            .Include(x => x.Items)
            .FirstAsync(x => x.Id == id && x.MarketId == marketId, cancellationToken);
        return MapToDto(r);
    }

    private static SaleReturnDto MapToDto(SaleReturn r) => new(
        r.Id,
        r.Number,
        r.SaleId,
        r.Sale?.SaleNumber ?? 0,
        r.Reason.ToString(),
        r.RefundMethod.ToString(),
        r.TotalAmount,
        r.Comment,
        r.User?.FullName,
        r.CreatedAt,
        r.Items.Select(i => new SaleReturnItemDto(i.ProductName, i.Quantity, i.UnitPrice, i.Quantity * i.UnitPrice)).ToList()
    );
}
