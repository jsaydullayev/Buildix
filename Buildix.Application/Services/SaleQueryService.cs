using Microsoft.EntityFrameworkCore;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Common;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Read-side of the sales domain. Market-scoped, read-only projections to DTOs.
/// See <see cref="ISaleQueryService"/>. Mapping is delegated to
/// <see cref="SaleMapper"/> so reads and writes project a Sale identically.
/// </summary>
public class SaleQueryService : ISaleQueryService
{
    // Hard cap to keep Excel export from streaming millions of rows into memory.
    // Callers that may legitimately want more should switch to a date-range
    // export (TODO: add ExportSalesByDateRangeAsync).
    private const int ExportMaxRows = 10_000;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly ILogger<SaleQueryService> _logger;

    public SaleQueryService(
        IUnitOfWork unitOfWork,
        IAppDbContext context,
        ICurrentMarketService currentMarketService,
        ILogger<SaleQueryService> logger)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _logger = logger;
    }

    public async Task<SaleDto?> GetSaleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.Id == id && s.MarketId == marketId,
            cancellationToken);

        var sale = sales.FirstOrDefault();

        if (sale is null)
            return null;

        return await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken);
    }

    public async Task<IEnumerable<SaleDto>> GetAllSalesAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Fetch ExportMaxRows + 1 so we can detect truncation without an extra COUNT(*).
        var sales = await _context.Sales
            .Include(s => s.Seller)
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
                .ThenInclude(si => si.Product)
            .Include(s => s.Payments)
            .Where(s => s.MarketId == marketId)
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .Take(ExportMaxRows + 1)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        if (sales.Count > ExportMaxRows)
        {
            _logger.LogWarning(
                "Sales export for market {MarketId} truncated at {Cap} rows. " +
                "Consider switching to a date-range export.",
                marketId, ExportMaxRows);
            sales = sales.Take(ExportMaxRows).ToList();
        }

        return sales.Select(SaleMapper.MapSale);
    }

    public async Task<PagedResult<SaleDto>> GetSalesPagedAsync(int page, int size, string? search = null, Guid? sellerId = null, string? paymentType = null, string? status = null, DateTime? from = null, DateTime? to = null, Guid? shiftId = null, CancellationToken cancellationToken = default)
    {
        // Clamp inputs — clients cannot ask for arbitrarily large pages or negative offsets.
        // Upper bound on `page` prevents (page - 1) * size int overflow on hostile input.
        if (page < 1) page = 1;
        if (page > 100_000) page = 100_000;
        if (size < 1) size = 50;
        if (size > 200) size = 200;

        var marketId = _currentMarketService.GetCurrentMarketId();

        var baseQuery = _context.Sales
            .Where(s => s.MarketId == marketId);

        // ── Filters (Продажи: qidiruv/оплата/продавец/период) ────────────────
        if (from.HasValue) baseQuery = baseQuery.Where(s => s.CreatedAt >= from.Value);
        if (to.HasValue) baseQuery = baseQuery.Where(s => s.CreatedAt <= to.Value);
        if (sellerId.HasValue) baseQuery = baseQuery.Where(s => s.SellerId == sellerId.Value);
        // Смена scoping: Sale.ShiftId is stamped from the open shift at creation,
        // so this alone already narrows to that shift's cashier — no extra seller
        // filter needed. Sales made before the column existed carry NULL and are
        // simply not part of any shift.
        if (shiftId.HasValue) baseQuery = baseQuery.Where(s => s.ShiftId == shiftId.Value);

        var statusFilter = !string.IsNullOrWhiteSpace(status) && Enum.TryParse<SaleStatus>(status, true, out var st)
            ? (SaleStatus?)st
            : null;

        if (statusFilter.HasValue)
            baseQuery = baseQuery.Where(s => s.Status == statusFilter.Value);
        else
            // Draft = kassada hali yakunlanmagan, qurilayotgan savat — u hali
            // "chek" emas, faqat band qilingan ЧЕК № . Sotuvlar ro'yxati moliyaviy
            // hujjatlar ro'yxati, shuning uchun draftlar u yerda ko'rinmaydi
            // (ularni kassir /my-drafts orqali davom ettiradi). Aks holda kassa
            // oynasi ochilib yopilganda paydo bo'lgan 0 so'mlik satrlar ro'yxatni
            // ifloslantirar va "chek yaratilmagan bo'lsa ham saqlanyapti" ko'rinishini
            // berardi. `status=Draft` bilan ataylab so'ralsa — ko'rsatamiz.
            baseQuery = baseQuery.Where(s => s.Status != SaleStatus.Draft);

        if (!string.IsNullOrWhiteSpace(paymentType))
        {
            if (string.Equals(paymentType, "Debt", StringComparison.OrdinalIgnoreCase))
                baseQuery = baseQuery.Where(s => s.Status == SaleStatus.Debt);
            else if (Enum.TryParse<PaymentType>(paymentType, true, out var pt))
                baseQuery = baseQuery.Where(s => s.Payments.Any(p => p.PaymentType == pt));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (int.TryParse(term, out var num))
            {
                baseQuery = baseQuery.Where(s => s.SaleNumber == num);
            }
            else
            {
                var lower = term.ToLower();
                baseQuery = baseQuery.Where(s =>
                    (s.Customer != null && s.Customer.FullName != null && s.Customer.FullName.ToLower().Contains(lower)) ||
                    (s.Customer != null && s.Customer.Phone.Contains(term)) ||
                    (s.Seller != null && s.Seller.FullName.ToLower().Contains(lower)) ||
                    s.SaleItems.Any(si => si.Product != null && si.Product.Name.ToLower().Contains(lower)));
            }
        }

        var total = await baseQuery.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult<SaleDto>.Empty(page, size);

        // Secondary sort by Id gives a stable order across pages when two sales
        // share a CreatedAt millisecond — without it boundary rows can dup/drop.
        var sales = await baseQuery
            .OrderByDescending(s => s.CreatedAt)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Include(s => s.Seller)
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
                .ThenInclude(si => si.Product)
            .Include(s => s.Payments)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var items = sales.Select(SaleMapper.MapSale).ToList();
        return PagedResult<SaleDto>.From(items, page, size, total);
    }

    public async Task<IEnumerable<SaleDto>> GetSalesByDateRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // [start, end) — half-open. Caller passes a UTC range where `end` is the
        // exclusive start of the day after the last requested day.
        var sales = await _context.Sales
            .Include(s => s.Seller)
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
                .ThenInclude(si => si.Product)
            .Include(s => s.Payments)
            .Where(s => s.MarketId == marketId && s.CreatedAt >= start && s.CreatedAt < end)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return sales.Select(SaleMapper.MapSale);
    }

    public async Task<IEnumerable<SaleDto>> GetDraftSalesBySellerAsync(Guid? sellerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var sales = await _context.Sales
            .Include(s => s.Seller)
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
                .ThenInclude(si => si.Product)
            .Include(s => s.Payments)
            .Where(s => s.MarketId == marketId && (sellerId == null || s.SellerId == sellerId) && s.Status == SaleStatus.Draft)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sales.Select(SaleMapper.MapSale);
    }

    public async Task<IEnumerable<SaleDto>> GetUnfinishedSalesBySellerAsync(Guid? sellerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var sales = await _context.Sales
            .Include(s => s.Seller)
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
                .ThenInclude(si => si.Product)
            .Include(s => s.Payments)
            .Where(s => s.MarketId == marketId && (sellerId == null || s.SellerId == sellerId) &&
                       (s.Status == SaleStatus.Draft || s.Status == SaleStatus.Debt || s.Status == SaleStatus.Paid))
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sales.Select(SaleMapper.MapSale);
    }

    public async Task<IEnumerable<CustomerDto>> GetDebtorsAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var debts = await _unitOfWork.Debts.FindAsync(
            d => d.MarketId == marketId && d.Status == DebtStatus.Open, q => q.Include(e => e.Customer), cancellationToken);

        // M-2: one row per customer with their TOTAL open debt. The previous code
        // projected one CustomerDto per Debt row and relied on Distinct() — but
        // two debts with different RemainingDebt are not equal, so a customer with
        // N open debts appeared N times, each showing a single debt, never the sum.
        var customers = debts
            .Where(d => d.Customer != null)
            .GroupBy(d => d.Customer!.Id)
            .Select(g =>
            {
                var c = g.First().Customer!;
                return new CustomerDto(c.Id, c.Phone ?? "", c.FullName, c.Comment, g.Sum(d => d.RemainingDebt));
            })
            .ToList();

        return customers;
    }
}
