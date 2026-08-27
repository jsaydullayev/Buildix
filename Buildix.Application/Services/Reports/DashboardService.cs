using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Dashboard aggregations, extracted verbatim from the former 2700-line
/// ReportService. Read-only rollups over Sales / SaleItems / Users / Shifts;
/// tenant-scoped; profit masked for non-Owner callers. Shared Tashkent date
/// helpers come from <see cref="ReportServiceBase"/>.
/// </summary>
public sealed class DashboardService(
    IUnitOfWork unitOfWork,
    ICurrentMarketService currentMarketService,
    IAppDbContext context,
    ITashkentClock clock)
    : ReportServiceBase(clock), IDashboardService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentMarketService _currentMarketService = currentMarketService;
    private readonly IAppDbContext _context = context;

    public async Task<DashboardSummaryDto> GetOwnerDashboardSummaryAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var now = DateTime.UtcNow;
        var overdueCutoff = now.AddDays(-14);

        // These five COUNT/SUM queries replace what the client used to do by
        // downloading GetAllCustomers + GetAllProducts + GetAllDebts in full
        // and folding them on the UI isolate. Definitions are kept identical to
        // that client logic so the dashboard numbers don't shift:
        //   - customerCount: non-deleted customers (== GetAllCustomers().length)
        //   - lowStockCount: Quantity <= MinThreshold (== Product.IsLowStock;
        //     intentionally NOT filtering IsDeleted, matching GetAllProducts)
        //   - pending debts: RemainingDebt > 0
        //   - overdue: past DueDate, or (no DueDate) created > 14 days ago
        var customerCount = await _context.Customers
            .AsNoTracking()
            .CountAsync(c => c.MarketId == marketId && !c.IsDeleted, cancellationToken);

        var lowStockCount = await _context.Products
            .AsNoTracking()
            .CountAsync(p => p.MarketId == marketId && p.Quantity <= p.MinThreshold, cancellationToken);

        var pending = _context.Debts
            .AsNoTracking()
            .Where(d => d.MarketId == marketId && d.RemainingDebt > 0);

        var pendingDebtsCount = await pending.CountAsync(cancellationToken);
        var pendingDebtsTotal =
            await pending.SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
        var overdueDebtsCount = await pending.CountAsync(
            d => (d.DueDate != null && d.DueDate < now) ||
                 (d.DueDate == null && d.CreatedAt < overdueCutoff),
            cancellationToken);

        return new DashboardSummaryDto(
            customerCount,
            lowStockCount,
            pendingDebtsCount,
            pendingDebtsTotal,
            overdueDebtsCount);
    }

    /// <summary>
    /// Last <paramref name="days"/> Tashkent calendar days, ending today, with
    /// revenue + profit + check count per day. Fills gaps with zero-points so
    /// the frontend can plot a continuous chart without gap-handling code.
    /// Profit is suppressed (returned as 0) for non-Owner callers.
    /// When <paramref name="compare"/> is true, also returns the total revenue
    /// for the equally-sized window immediately preceding [current window],
    /// so the dashboard can show a week-over-week delta without a second
    /// round-trip.
    /// </summary>
    public async Task<WeeklySeriesDto> GetWeeklySeriesAsync(
        int days, bool compare = false, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        // Clamp to [1, 30] — frontend asks for 7 by default; 30 is a hard cap
        // so a misbehaving client can't trigger a month-long full table scan.
        if (days < 1) days = 1;
        if (days > 30) days = 30;

        var marketId = _currentMarketService.GetCurrentMarketId();
        var todayLocal = _clock.TodayLocal;
        var rangeStartLocal = todayLocal.AddDays(-(days - 1));
        var rangeStartUtc = ToUtcDate(rangeStartLocal);
        var (_, rangeEndUtc) = GetUtcDateRange(todayLocal);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= rangeStartUtc && s.CreatedAt < rangeEndUtc &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                 s.MarketId == marketId, q => q.Include(e => e.SaleItems), cancellationToken);

        var includeProfit = canViewProfit;
        var byDay = new Dictionary<DateTime, (decimal revenue, decimal profit, int count)>(days);

        foreach (var sale in sales)
        {
            // Bucket sales into the Tashkent calendar day they belong to.
            // CreatedAt is UTC; offset back to local before flooring to date.
            var localDay = _clock.ToLocal(sale.CreatedAt).Date;

            var current = byDay.TryGetValue(localDay, out var existing)
                ? existing
                : (revenue: 0m, profit: 0m, count: 0);
            decimal saleProfit = 0;
            if (includeProfit)
            {
                foreach (var item in sale.SaleItems)
                {
                    var costPrice = item.IsExternal ? item.ExternalCostPrice : item.CostPrice;
                    saleProfit += (item.SalePrice - costPrice) * item.Quantity;
                }
            }
            byDay[localDay] = (
                current.revenue + sale.TotalAmount,
                current.profit + saleProfit,
                current.count + 1);
        }

        var points = new List<DailyPoint>(days);
        decimal currentTotal = 0;
        for (var i = 0; i < days; i++)
        {
            var localDay = rangeStartLocal.AddDays(i).Date;
            var utcStart = ToUtcDate(localDay);
            var bucket = byDay.TryGetValue(localDay, out var v) ? v : (0m, 0m, 0);
            points.Add(new DailyPoint(utcStart, bucket.Item1, bucket.Item2, bucket.Item3));
            currentTotal += bucket.Item1;
        }

        // Optional second pass for the previous equally-sized window so the
        // frontend's ChartCard footer can render "↑/↓ X% vs last week".
        // We deliberately query a separate batch (rather than widening the
        // first one to 2× the range) to keep memory bounded when days=30.
        decimal? previousTotal = null;
        if (compare)
        {
            var prevStartLocal = rangeStartLocal.AddDays(-days);
            var prevStartUtc = ToUtcDate(prevStartLocal);
            var prevEndUtc = rangeStartUtc;

            var prevSales = await _unitOfWork.Sales.FindAsync(
                s => s.CreatedAt >= prevStartUtc && s.CreatedAt < prevEndUtc &&
                     s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                     s.MarketId == marketId,
                cancellationToken);

            previousTotal = prevSales.Sum(s => s.TotalAmount);
        }

        return new WeeklySeriesDto(points, currentTotal, previousTotal);
    }

    /// <summary>
    /// Top-N products in the selected period, ranked by quantity / revenue /
    /// profit. Tenant-scoped; profit hidden for non-Owner callers.
    /// </summary>
    public async Task<TopProductsDto> GetTopProductsAsync(
        string period, string sortBy, int limit,
        bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 50) limit = 50;

        var marketId = _currentMarketService.GetCurrentMarketId();
        var (rangeStartUtc, rangeEndUtc) = ResolvePeriodRange(period);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= rangeStartUtc && s.CreatedAt < rangeEndUtc &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                 s.MarketId == marketId, q => q.Include(e => e.SaleItems), cancellationToken);

        // Fallback: if `today` returned nothing (typical for fresh shops at
        // 9 AM, or shops with no sales yet today), widen to the rolling-week
        // window so the dashboard isn't a blank box. We mutate `period` so the
        // returned DTO advertises the actual range used.
        var effectivePeriod = period;
        if ((period?.ToLowerInvariant() == "today") && !sales.Any())
        {
            (rangeStartUtc, rangeEndUtc) = ResolvePeriodRange("week");
            effectivePeriod = "week";
            sales = await _unitOfWork.Sales.FindAsync(
                s => s.CreatedAt >= rangeStartUtc && s.CreatedAt < rangeEndUtc &&
                     s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                     s.MarketId == marketId, q => q.Include(e => e.SaleItems), cancellationToken);
        }

        var includeProfit = canViewProfit;

        // Group line items by ProductId; track distinct sellers per product.
        var byProduct = new Dictionary<Guid, (decimal qty, decimal revenue, decimal profit, HashSet<Guid> sellers)>();
        foreach (var sale in sales)
        {
            foreach (var item in sale.SaleItems)
            {
                // Skip external one-off products — they don't have a stable
                // ProductId across sales, so "top external one-off" would
                // be meaningless noise in the ranking. ProductId is also
                // guaranteed non-null after this gate.
                if (item.IsExternal || item.ProductId == null) continue;

                var key = item.ProductId.Value;
                if (!byProduct.TryGetValue(key, out var agg))
                {
                    agg = (0m, 0m, 0m, new HashSet<Guid>());
                }
                agg.qty += item.Quantity;
                agg.revenue += item.SalePrice * item.Quantity;
                if (includeProfit)
                {
                    var costPrice = item.IsExternal ? item.ExternalCostPrice : item.CostPrice;
                    agg.profit += (item.SalePrice - costPrice) * item.Quantity;
                }
                agg.sellers.Add(sale.SellerId);
                byProduct[key] = agg;
            }
        }

        // Resolve category names in one batch — saves N round-trips when the
        // ranking spans many distinct categories.
        var productIds = byProduct.Keys.ToList();
        var products = await _unitOfWork.Products.FindAsync(
            p => productIds.Contains(p.Id) && p.MarketId == marketId, q => q.Include(e => e.Category), cancellationToken);
        var productCategory = products.ToDictionary(
            p => p.Id,
            p => p.Category?.Name ?? string.Empty);
        var productName = products.ToDictionary(p => p.Id, p => p.Name);

        // Sort by the requested key; ties broken by quantity desc.
        var sortKey = sortBy?.ToLowerInvariant() ?? "quantity";
        IEnumerable<KeyValuePair<Guid, (decimal qty, decimal revenue, decimal profit, HashSet<Guid> sellers)>> ordered = sortKey switch
        {
            "revenue" => byProduct.OrderByDescending(p => p.Value.revenue).ThenByDescending(p => p.Value.qty),
            "profit" => byProduct.OrderByDescending(p => p.Value.profit).ThenByDescending(p => p.Value.qty),
            _ => byProduct.OrderByDescending(p => p.Value.qty).ThenByDescending(p => p.Value.revenue),
        };

        var rows = new List<TopProductRow>();
        var rank = 1;
        foreach (var (id, agg) in ordered.Take(limit))
        {
            rows.Add(new TopProductRow(
                Rank: rank++,
                ProductId: id.ToString(),
                Name: productName.TryGetValue(id, out var n) ? n : string.Empty,
                Category: productCategory.TryGetValue(id, out var c) ? c : string.Empty,
                Sellers: agg.sellers.Count,
                Quantity: agg.qty,
                Revenue: agg.revenue,
                Profit: includeProfit ? agg.profit : null));
        }

        // Echo the *resolved* period, not the requested one — that way when
        // today→week fallback kicked in above the UI knows to re-label the
        // panel as "Bu hafta" instead of misleadingly "Bugun".
        return new TopProductsDto(effectivePeriod ?? "month", sortKey, rows);
    }

    /// <summary>
    /// Per-staff sales metrics for the period. Includes staff with zero sales
    /// so the page can show the whole team (otherwise a quiet seller would
    /// silently disappear from the leaderboard). Shift counts come from the
    /// Shift entity — sessions opened inside the period count; the
    /// <c>IsActiveShift</c> flag also catches sessions that opened earlier
    /// and are still open right now.
    /// </summary>
    public async Task<StaffPerformanceDto> GetStaffPerformanceAsync(
        string period, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var (rangeStartUtc, rangeEndUtc) = ResolvePeriodRange(period);

        var users = await _unitOfWork.Users.FindAsync(
            u => u.MarketId == marketId &&
                 (u.Role == Role.Seller || u.Role == Role.Admin || u.Role == Role.Owner),
            cancellationToken);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= rangeStartUtc && s.CreatedAt < rangeEndUtc &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                 s.MarketId == marketId,
            cancellationToken);

        var bySeller = sales
            .GroupBy(s => s.SellerId)
            .ToDictionary(g => g.Key, g => (count: g.Count(), revenue: g.Sum(s => s.TotalAmount)));

        // Pull the shifts in one round-trip and bucket them by user. The
        // predicate keeps it cheap: "either opened in this period, or still
        // open right now". A seller who clocked in last week and never
        // closed shows IsActiveShift=true even if ShiftCount stays 0 for
        // the current week.
        var shifts = (await _unitOfWork.Shifts.FindAsync(
            sh => sh.MarketId == marketId &&
                  ((sh.OpenedAt >= rangeStartUtc && sh.OpenedAt < rangeEndUtc)
                   || sh.ClosedAt == null),
            cancellationToken)).ToList();

        var shiftCountByUser = shifts
            .Where(sh => sh.OpenedAt >= rangeStartUtc && sh.OpenedAt < rangeEndUtc)
            .GroupBy(sh => sh.UserId)
            .ToDictionary(g => g.Key, g => g.Count());

        var activeShiftUsers = shifts
            .Where(sh => sh.ClosedAt == null)
            .Select(sh => sh.UserId)
            .ToHashSet();

        var rows = new List<StaffRow>();
        foreach (var u in users)
        {
            var stats = bySeller.TryGetValue(u.Id, out var v) ? v : (0, 0m);
            rows.Add(new StaffRow(
                Rank: 0, // assigned after sort
                UserId: u.Id.ToString(),
                FullName: u.FullName,
                Role: u.Role.ToString(),
                SaleCount: stats.Item1,
                Revenue: stats.Item2,
                AverageCheck: stats.Item1 == 0 ? 0m : stats.Item2 / stats.Item1,
                ShiftCount: shiftCountByUser.TryGetValue(u.Id, out var sc) ? sc : 0,
                IsActiveShift: activeShiftUsers.Contains(u.Id)
            ));
        }

        // Sort by Revenue desc, then FullName asc for stable ordering of zero-sales staff.
        var sorted = rows
            .OrderByDescending(r => r.Revenue)
            .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
            .Select((r, i) => r with { Rank = i + 1 })
            .ToList();

        return new StaffPerformanceDto(period ?? "week", sorted);
    }

    /// <summary>
    /// One seller's own metrics. Same shape as a single <see cref="StaffRow"/>
    /// but scoped to a single user, plus a derived "first sale today" timestamp
    /// for the Seller dashboard's shift-duration card.
    /// </summary>
    public async Task<MyPerformanceDto> GetMyPerformanceAsync(
        Guid userId, string period, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var (rangeStartUtc, rangeEndUtc) = ResolvePeriodRange(period);

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        var fullName = user?.FullName ?? string.Empty;

        // Only this seller's non-draft, non-cancelled sales in the period.
        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= rangeStartUtc && s.CreatedAt < rangeEndUtc &&
                 s.SellerId == userId &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                 s.MarketId == marketId,
            cancellationToken);

        var salesList = sales.ToList();
        var saleCount = salesList.Count;
        var revenue = salesList.Sum(s => s.TotalAmount);
        var averageCheck = saleCount == 0 ? 0m : revenue / saleCount;

        DateTime? firstSaleAt = saleCount == 0
            ? null
            : salesList.Min(s => s.CreatedAt);

        // Real shift tracking: sum the worked minutes of every Shift the seller
        // opened within the period (open shifts count up to "now"). Falls back
        // to the "minutes since first sale" heuristic when the seller has no
        // recorded shifts yet — so the dashboard never regresses to 0.
        var shifts = await _unitOfWork.Shifts.FindAsync(
            s => s.UserId == userId && s.MarketId == marketId &&
                 s.OpenedAt >= rangeStartUtc && s.OpenedAt < rangeEndUtc,
            cancellationToken);
        var shiftList = shifts.ToList();

        int shiftMinutes;
        if (shiftList.Count > 0)
        {
            shiftMinutes = shiftList.Sum(s =>
            {
                var minutes = ((s.ClosedAt ?? DateTime.UtcNow) - s.OpenedAt).TotalMinutes;
                return minutes > 0 ? (int)minutes : 0;
            });
        }
        else if (firstSaleAt is { } first)
        {
            var elapsed = DateTime.UtcNow - first;
            // Clamp to non-negative — clock-skew between server and DB can
            // produce small negatives that would render as "-3 min".
            shiftMinutes = elapsed.TotalMinutes > 0 ? (int)elapsed.TotalMinutes : 0;
        }
        else
        {
            shiftMinutes = 0;
        }

        return new MyPerformanceDto(
            Period: period ?? "today",
            UserId: userId.ToString(),
            FullName: fullName,
            SaleCount: saleCount,
            Revenue: revenue,
            AverageCheck: averageCheck,
            FirstSaleAtUtc: firstSaleAt,
            ShiftDurationMinutes: shiftMinutes);
    }

    /// <summary>
    /// Map a string period token to a Tashkent-anchored UTC date range.
    /// Defaults to "month" on any unrecognised value so callers don't see
    /// errors from typos — the response still echoes the resolved period.
    /// </summary>
    private (DateTime StartUtc, DateTime EndUtc) ResolvePeriodRange(string? period)
    {
        var todayLocal = _clock.TodayLocal;
        var (todayStart, todayEnd) = GetUtcDateRange(todayLocal);

        return (period?.ToLowerInvariant()) switch
        {
            "today" => (todayStart, todayEnd),
            "week" => (ToUtcDate(todayLocal.AddDays(-6)), todayEnd),
            "year" => (ToUtcDate(new DateTime(todayLocal.Year, 1, 1)), todayEnd),
            _ => (ToUtcDate(new DateTime(todayLocal.Year, todayLocal.Month, 1)), todayEnd), // month (default)
        };
    }
}
