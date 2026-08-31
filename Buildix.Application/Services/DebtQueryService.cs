using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Read-side of the debt domain. Market-scoped, read-only projections to DTOs.
/// See <see cref="IDebtQueryService"/>. Mapping delegated to <see cref="DebtMapper"/>.
/// </summary>
public class DebtQueryService : IDebtQueryService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarket;
    private readonly ITashkentClock _clock;

    public DebtQueryService(IAppDbContext context, ICurrentMarketService currentMarket, ITashkentClock clock)
    {
        _context = context;
        _currentMarket = currentMarket;
        _clock = clock;
    }

    public async Task<IEnumerable<DebtDto>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();

        var debts = await _context.Debts
            .AsNoTracking()
            .Include(d => d.Sale)
                .ThenInclude(s => s!.SaleItems)
                    .ThenInclude(si => si.Product)
            .Include(d => d.Customer)
            .Where(d => d.CustomerId == customerId && d.Status == DebtStatus.Open && d.MarketId == marketId)
            .OrderByDescending(d => d.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return debts.Select(DebtMapper.MapToDto).ToList();
    }

    public async Task<decimal> GetCustomerTotalAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();

        // Aggregate in the DB so we never materialise the full row set just to sum.
        return await _context.Debts
            .Where(d => d.CustomerId == customerId && d.Status == DebtStatus.Open && d.MarketId == marketId)
            .SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
    }

    public async Task<IEnumerable<DebtDto>> ListAsync(DebtStatus? status, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();

        var query = _context.Debts
            .AsNoTracking()
            .Include(d => d.Sale)
                .ThenInclude(s => s!.SaleItems)
                    .ThenInclude(si => si.Product)
            .Include(d => d.Customer)
            .Where(d => d.MarketId == marketId);

        if (status.HasValue)
            query = query.Where(d => d.Status == status.Value);

        var debts = await query
            .OrderByDescending(d => d.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return debts.Select(DebtMapper.MapToDto).ToList();
    }

    public async Task<IReadOnlyList<DebtorSummaryDto>> GetDebtorSummariesAsync(string? search, string? due, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var now = DateTime.UtcNow;

        // Open, still-owed debts joined to their customer, grouped per customer.
        var query = _context.Debts
            .AsNoTracking()
            .Include(d => d.Customer)
            .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.RemainingDebt > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var lower = term.ToLower();
            query = query.Where(d =>
                (d.Customer.FullName != null && d.Customer.FullName.ToLower().Contains(lower)) ||
                d.Customer.Phone.Contains(term));
        }

        var rows = await query.ToListAsync(cancellationToken);

        var summaries = rows
            .GroupBy(d => d.CustomerId)
            .Select(g =>
            {
                var c = g.First().Customer;
                var nearest = g.Where(d => d.DueDate.HasValue).Select(d => d.DueDate!.Value).DefaultIfEmpty().Min();
                var nearestDue = g.Any(d => d.DueDate.HasValue) ? nearest : (DateTime?)null;
                var overdue = g.Any(d => d.DueDate.HasValue && d.DueDate!.Value < now);
                return new DebtorSummaryDto(
                    g.Key,
                    c.FullName,
                    c.Phone,
                    c.CustomerType.ToString(),
                    g.Sum(d => d.RemainingDebt),
                    g.Count(),
                    nearestDue,
                    overdue);
            })
            .ToList();

        // Due filter (client-facing "Все/Просроченные/Сегодня/Предстоящие").
        summaries = due switch
        {
            "overdue" => summaries.Where(s => s.IsOverdue).ToList(),
            "today" => summaries.Where(s => s.NearestDueDate.HasValue && s.NearestDueDate.Value.Date == now.Date).ToList(),
            "upcoming" => summaries.Where(s => s.NearestDueDate.HasValue && s.NearestDueDate.Value.Date > now.Date).ToList(),
            _ => summaries,
        };

        // Overdue first, then soonest due, then largest balance.
        return summaries
            .OrderByDescending(s => s.IsOverdue)
            .ThenBy(s => s.NearestDueDate ?? DateTime.MaxValue)
            .ThenByDescending(s => s.RemainingDebt)
            .ToList();
    }

    /// <summary>Chek kartochkasidagi tovar qatori — faqat ko'rsatish uchun.</summary>
    private readonly record struct DebtCheckItem(Guid SaleId, string? Name, decimal Quantity);

    public async Task<IReadOnlyList<DebtCheckDto>> GetDebtChecksAsync(string? search, string? due, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var now = DateTime.UtcNow;

        // Tovar grafigi ATAYLAB yuklanmaydi. Ilgari bu yerda
        // `.ThenInclude(SaleItems).ThenInclude(Product)` turardi va ekran
        // ochilganda har bir ochiq qarzning HAMMA qatorlari, ularning har
        // biri uchun esa to'liq `Product` yozuvi tortilardi — barcha
        // ustunlari bilan. Ekranda esa faqat ikkita tovar NOMI ko'rinadi.
        // Yuzta qarzli do'konda bu bir necha ming ortiqcha qator degani.
        var query = _context.Debts
            .AsNoTracking()
            .Include(d => d.Customer)
            .Include(d => d.Sale)
            .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.RemainingDebt > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var lower = term.ToLower();
            var asNumber = int.TryParse(term, out var num) ? num : (int?)null;
            query = query.Where(d =>
                (d.Customer.FullName != null && d.Customer.FullName.ToLower().Contains(lower)) ||
                d.Customer.Phone.Contains(term) ||
                (asNumber != null && d.Sale != null && d.Sale.SaleNumber == asNumber));
        }

        var rows = await query.ToListAsync(cancellationToken);

        // Tovar nomlari ALOHIDA so'rov bilan va faqat kerakli ustunlar:
        // chek raqami, nom va miqdor. Entity'ni to'liq yuklashning ma'nosi
        // yo'q — ekranga faqat nom chiqadi.
        var saleIds = rows.Select(d => d.SaleId).Distinct().ToList();
        var itemsBySale = saleIds.Count == 0
            ? new Dictionary<Guid, List<DebtCheckItem>>()
            : (await _context.SaleItems
                .AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .Select(si => new DebtCheckItem(
                    si.SaleId,
                    si.IsExternal ? si.ExternalProductName : si.Product!.Name,
                    si.Quantity))
                .ToListAsync(cancellationToken))
              .GroupBy(x => x.SaleId)
              .ToDictionary(g => g.Key, g => g.ToList());

        // «несколько долгов» — mijozning nechta ochiq qarzi borligi (badge uchun).
        var countByCustomer = rows.GroupBy(d => d.CustomerId).ToDictionary(g => g.Key, g => g.Count());

        var checks = rows.Select(d =>
        {
            var items = itemsBySale.GetValueOrDefault(d.SaleId) ?? [];
            var named = items.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
            var summary = string.Join(", ", named.Take(2).Select(i => $"{i.Name} ×{i.Quantity:0.##}"));
            if (named.Count > 2)
                summary += $" +{named.Count - 2}";

            return new DebtCheckDto(
                d.Id,
                d.SaleId,
                d.Sale?.SaleNumber ?? 0,
                d.CustomerId,
                d.Customer.FullName,
                d.Customer.Phone,
                d.Customer.CustomerType.ToString(),
                d.Sale?.CreatedAt ?? d.CreatedAt,
                d.DueDate,
                d.DueDate.HasValue && d.DueDate.Value < now,
                d.TotalDebt,
                d.RemainingDebt,
                summary,
                items.Count,
                countByCustomer.TryGetValue(d.CustomerId, out var cc) ? cc : 1);
        }).ToList();

        checks = due switch
        {
            "overdue" => checks.Where(c => c.IsOverdue).ToList(),
            "today" => checks.Where(c => c.DueDate.HasValue && c.DueDate.Value.Date == now.Date).ToList(),
            "upcoming" => checks.Where(c => c.DueDate.HasValue && c.DueDate.Value.Date > now.Date).ToList(),
            _ => checks,
        };

        // Muddati o'tganlar birinchi, keyin eng yaqin muddat, keyin eng katta qoldiq.
        return checks
            .OrderByDescending(c => c.IsOverdue)
            .ThenBy(c => c.DueDate ?? DateTime.MaxValue)
            .ThenByDescending(c => c.RemainingDebt)
            .ToList();
    }

    public async Task<DebtSummaryStatsDto> GetSummaryStatsAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var now = DateTime.UtcNow;
        // L-1: anchor "today"/"this month" to the Tashkent business day, not UTC —
        // otherwise 00:00–05:00 Tashkent repayments fall into the previous UTC day
        // and the owner's "paid today" reads short every early morning.
        var todayLocal = _clock.TodayLocal;
        var todayStart = _clock.LocalDayToUtcRange(todayLocal).UtcStart;
        var monthStart = _clock.LocalDayToUtcRange(new DateTime(todayLocal.Year, todayLocal.Month, 1)).UtcStart;

        var openDebts = _context.Debts
            .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.RemainingDebt > 0);

        var totalDebt = await openDebts.SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
        var overdue = openDebts.Where(d => d.DueDate.HasValue && d.DueDate!.Value < now);
        var overdueTotal = await overdue.SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
        var overdueCount = await overdue.Select(d => d.CustomerId).Distinct().CountAsync(cancellationToken);
        var customerCount = await openDebts.Select(d => d.CustomerId).Distinct().CountAsync(cancellationToken);

        // Debt repayments = payments recorded against sales that carry a debt.
        var debtPayments = _context.Payments
            .Where(p => p.Sale != null && p.Sale.MarketId == marketId && p.Sale.Debt != null);
        var paidToday = await debtPayments
            .Where(p => p.CreatedAt >= todayStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var paidThisMonth = await debtPayments
            .Where(p => p.CreatedAt >= monthStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        return new DebtSummaryStatsDto(totalDebt, overdueTotal, overdueCount, customerCount, paidToday, paidThisMonth);
    }

    public async Task<IReadOnlyList<DebtPaymentTodayDto>> GetTodayPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var todayStart = _clock.LocalDayToUtcRange(_clock.TodayLocal).UtcStart;

        // Bugungi qarz-to'lovlari — qarzli sotuvga yozilgan musbat to'lovlar
        // (refund manfiy → chiqariladi). «Остаток долга» = joriy qoldiq.
        return await _context.Payments
            .Where(p => p.Sale != null && p.Sale.MarketId == marketId && p.Sale.Debt != null
                && p.CreatedAt >= todayStart && p.Amount > 0)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new DebtPaymentTodayDto(
                p.CreatedAt,
                p.Sale!.Customer != null ? p.Sale.Customer.FullName : null,
                p.Sale.Customer != null ? p.Sale.Customer.Phone : null,
                p.PaymentType.ToString(),
                p.Amount,
                p.Sale.Debt!.RemainingDebt))
            .ToListAsync(cancellationToken);
    }
}
