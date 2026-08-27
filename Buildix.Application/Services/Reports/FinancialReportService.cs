using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Extensions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Financial rollups — today/week/month profit summary and the cash-vs-card
/// balance — extracted verbatim from the former 2700-line ReportService.
/// Profit figures are Owner-gated at the call site.
/// </summary>
public sealed class FinancialReportService(
    IUnitOfWork unitOfWork,
    ICurrentMarketService currentMarketService,
    IAppDbContext context,
    ITashkentClock clock)
    : ReportServiceBase(clock), IFinancialReportService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentMarketService _currentMarketService = currentMarketService;
    private readonly IAppDbContext _context = context;

    public async Task<ProfitSummaryDto> GetProfitSummaryAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var todayLocal = _clock.TodayLocal;

        // Today (Tashkent calendar day -> UTC range)
        var (todayStart, todayEnd) = GetUtcDateRange(todayLocal);

        // Week = rolling 7-day window (last 7 days including today), anchored to
        // Tashkent local midnight. Rolling-7d matches user intuition
        // ("shu hafta = oxirgi 7 kun") and avoids the Sunday/Monday reset that
        // an ISO-week anchor causes.
        var weekStart = ToUtcDate(todayLocal.AddDays(-6));
        var monthStart = ToUtcDate(new DateTime(todayLocal.Year, todayLocal.Month, 1));

        // P2 — the previous version loaded every Sale + SaleItem for today,
        // week, month, AND all-time into memory (the all-time fetch is
        // O(history) — fatal once a market has a year of data) just to sum
        // profit. Replace with one DB-side aggregation that emits a single
        // SQL statement with four conditional SUMs.
        //
        // EF translates the CASE WHEN into PG's FILTER clause. Profit per
        // item = (SalePrice − effectiveCost) × Quantity, where effectiveCost
        // is ExternalCostPrice for tashqi mahsulot and CostPrice for normal
        // products. allTime always passes the date filter (constant true).
        var summary = await _context.SaleItems
            .AsNoTracking()
            .Where(si => si.Sale.MarketId == marketId
                      && si.Sale.Status != SaleStatus.Cancelled
                      && si.Sale.Status != SaleStatus.Draft)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Today = g.Sum(si =>
                    si.Sale.CreatedAt >= todayStart && si.Sale.CreatedAt < todayEnd
                        ? ((si.IsExternal ? si.SalePrice - si.ExternalCostPrice
                                          : si.SalePrice - si.CostPrice) * si.Quantity)
                        : 0m),
                Week = g.Sum(si =>
                    si.Sale.CreatedAt >= weekStart
                        ? ((si.IsExternal ? si.SalePrice - si.ExternalCostPrice
                                          : si.SalePrice - si.CostPrice) * si.Quantity)
                        : 0m),
                Month = g.Sum(si =>
                    si.Sale.CreatedAt >= monthStart
                        ? ((si.IsExternal ? si.SalePrice - si.ExternalCostPrice
                                          : si.SalePrice - si.CostPrice) * si.Quantity)
                        : 0m),
                All = g.Sum(si =>
                    (si.IsExternal ? si.SalePrice - si.ExternalCostPrice
                                   : si.SalePrice - si.CostPrice) * si.Quantity),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Sale-level chegirma (skidka): the item sums above are computed from
        // GROSS item revenue, so every window is overstated by the discounts on
        // its sales. Aggregate them with the SAME market/status filters and the
        // SAME date windows so the two line up exactly, then subtract.
        var discounts = await _context.Sales
            .AsNoTracking()
            .Where(s => s.MarketId == marketId
                     && s.Status != SaleStatus.Cancelled && !s.IsOpeningBalance
                     && s.Status != SaleStatus.Draft)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Today = g.Sum(s =>
                    s.CreatedAt >= todayStart && s.CreatedAt < todayEnd ? s.DiscountAmount : 0m),
                Week = g.Sum(s => s.CreatedAt >= weekStart ? s.DiscountAmount : 0m),
                Month = g.Sum(s => s.CreatedAt >= monthStart ? s.DiscountAmount : 0m),
                All = g.Sum(s => s.DiscountAmount),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new ProfitSummaryDto(
            (summary?.Today ?? 0m) - (discounts?.Today ?? 0m),
            (summary?.Week ?? 0m) - (discounts?.Week ?? 0m),
            (summary?.Month ?? 0m) - (discounts?.Month ?? 0m),
            (summary?.All ?? 0m) - (discounts?.All ?? 0m));
    }

    public async Task<CashBalanceDto> GetCashBalanceAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // "Today" anchored to Tashkent calendar day (00:00–24:00 local), not UTC.
        var (todayStart, todayEnd) = GetUtcDateRange(_clock.TodayLocal);

        // Get all payments for today
        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= todayStart && s.CreatedAt < todayEnd &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.Payments), cancellationToken);

        decimal cashInRegister = 0;
        decimal cardPayments = 0;
        decimal totalRefunds = 0;

        foreach (var sale in sales)
        {
            foreach (var payment in sale.Payments)
            {
                if (payment.Amount < 0)
                {
                    // Negative payment = refund, subtract from the appropriate balance
                    if (payment.PaymentType == PaymentType.Cash)
                    {
                        cashInRegister += payment.Amount;  // This will subtract since payment.Amount is negative
                    }
                    else if (payment.PaymentType == PaymentType.Terminal)
                    {
                        cardPayments += payment.Amount;  // This will subtract
                    }
                    totalRefunds += Math.Abs(payment.Amount);
                }
                else
                {
                    // Positive payment = actual payment
                    if (payment.PaymentType == PaymentType.Cash)
                    {
                        cashInRegister += payment.Amount;
                    }
                    else if (payment.PaymentType == PaymentType.Terminal)
                    {
                        cardPayments += payment.Amount;
                    }
                }
            }
        }

        return new CashBalanceDto(
            cashInRegister,
            cardPayments,
            cashInRegister + cardPayments
        );
    }


}
