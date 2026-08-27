using System.Globalization;
using System.Text;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Assembles the Telegram day summary. Runs both from the bot webhook (owner
/// typed /kunlik) and from the nightly background job, so it never touches
/// <see cref="ICurrentMarketService"/> — every query is market-filtered by hand.
/// Messages are Russian + HTML, matching the existing shift/withdrawal notices.
/// </summary>
public class TelegramDailySummaryService : ITelegramDailySummaryService
{
    private const int LowStockListLimit = 5;
    private const int TopProductLimit = 3;

    private readonly IAppDbContext _db;
    private readonly ITashkentClock _clock;

    public TelegramDailySummaryService(IAppDbContext db, ITashkentClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<string?> BuildAsync(int marketId, DateTime localDate, DailySummaryOptions options,
        CancellationToken cancellationToken = default)
    {
        var market = await _db.Markets.AsNoTracking()
            .Where(m => m.Id == marketId)
            .Select(m => new { m.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (market is null) return null;

        var (dayStart, dayEnd) = _clock.LocalDayToUtcRange(localDate);

        // ── Sales for the day (Draft/Cancelled never count as revenue) ───────
        // SellerId scopes the figures to one cashier's own receipts: without
        // data.allSalesView they must not read the shop's total takings.
        var daySales = _db.Sales.AsNoTracking().Where(s =>
            s.MarketId == marketId && !s.IsDeleted &&
            s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled && !s.IsOpeningBalance &&
            s.CreatedAt >= dayStart && s.CreatedAt < dayEnd &&
            (options.SellerId == null || s.SellerId == options.SellerId));

        var checkCount = await daySales.CountAsync(cancellationToken);
        var revenue = await daySales.SumAsync(s => (decimal?)s.TotalAmount, cancellationToken) ?? 0m;

        // Tender split comes from Payments (a receipt may be paid with several).
        var saleIds = await daySales.Select(s => s.Id).ToListAsync(cancellationToken);
        var tenders = saleIds.Count == 0
            ? []
            : await _db.Payments.AsNoTracking()
                .Where(p => p.MarketId == marketId && saleIds.Contains(p.SaleId) && p.Amount > 0)
                .GroupBy(p => p.PaymentType)
                .Select(g => new { Type = g.Key, Sum = g.Sum(x => x.Amount) })
                .ToListAsync(cancellationToken);

        decimal Tender(PaymentType type) => tenders.FirstOrDefault(x => x.Type == type)?.Sum ?? 0m;
        var cashIn = Tender(PaymentType.Cash);
        var cardIn = Tender(PaymentType.Terminal) + Tender(PaymentType.Click) + Tender(PaymentType.Transfer);

        // Debt sold today — the part of revenue that did not become money.
        // Scoped through the day's sale ids so it follows options.SellerId: a
        // market-wide sum here would print the shop's credit sales under a
        // «Мои продажи» heading.
        var debtSold = saleIds.Count == 0
            ? 0m
            : await _db.Debts.AsNoTracking()
                .Where(d => d.MarketId == marketId && saleIds.Contains(d.SaleId))
                .SumAsync(d => (decimal?)d.TotalDebt, cancellationToken) ?? 0m;

        // ── Profit — data.profit only ────────────────────────────────────────
        // Not merely hidden at render time: the figure is never even queried for
        // a reader who may not see it. Effective cost mirrors
        // SaleItem.EffectiveCostPrice — external items carry their cost in
        // ExternalCostPrice while CostPrice stays 0.
        var profit = 0m;
        if (options.IncludeProfit && saleIds.Count > 0)
            profit = await _db.SaleItems.AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .SumAsync(si => (decimal?)((si.SalePrice - (si.IsExternal ? si.ExternalCostPrice : si.CostPrice)) * si.Quantity),
                    cancellationToken) ?? 0m;
        var margin = revenue > 0 ? profit / revenue * 100m : 0m;

        // ── Returns for the day ──────────────────────────────────────────────
        // Also seller-scoped: a return belongs to the receipt it reverses, so
        // restricting to this reader's sales keeps «Возвраты» consistent with
        // the «Мои продажи» figures above it.
        // SaleReturn has no soft-delete flag — returns are permanent records.
        var returnsQuery = _db.SaleReturns.AsNoTracking()
            .Where(r => r.MarketId == marketId && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd);
        if (options.SellerId is not null)
            returnsQuery = returnsQuery.Where(r => r.Sale != null && r.Sale.SellerId == options.SellerId);
        var returns = await returnsQuery
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Sum = g.Sum(x => x.TotalAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        // ── Cash — data.cashBalance only ─────────────────────────────────────
        var cashBalance = 0m;
        if (options.IncludeCash)
            cashBalance = await _db.CashRegisters.AsNoTracking()
                .Where(c => c.MarketId == marketId)
                .Select(c => (decimal?)c.CurrentBalance)
                .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        // ── Debts — debts.access only ────────────────────────────────────────
        var debtTotal = 0m;
        decimal debtPaidToday = 0m;
        (int Count, decimal Sum)? overdue = null;
        if (options.IncludeDebts)
        {
            var openDebts = _db.Debts.AsNoTracking()
                .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.RemainingDebt > 0);
            debtTotal = await openDebts.SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
            var now = _clock.UtcNow;
            var od = await openDebts
                .Where(d => d.DueDate != null && d.DueDate < now)
                .GroupBy(d => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(x => x.RemainingDebt) })
                .FirstOrDefaultAsync(cancellationToken);
            if (od is not null) overdue = (od.Count, od.Sum);

            // Debt repayments collected today — payments booked against a sale that
            // carries a debt. Same definition as DebtQueryService.GetSummaryStatsAsync
            // so the bot and the Долги screen never disagree.
            debtPaidToday = await _db.Payments.AsNoTracking()
                .Where(p => p.Sale != null && p.Sale.MarketId == marketId && p.Sale.Debt != null
                    && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        }

        // ── Stock signals — products.access only ─────────────────────────────
        var stockAlerts = new List<(string Name, decimal Quantity)>();
        var lowStockTotal = 0;
        var outOfStock = 0;
        if (options.IncludeStock)
        {
            var lowQuery = _db.Products.AsNoTracking()
                .Where(p => p.MarketId == marketId && !p.IsDeleted && !p.IsHidden && p.Quantity <= p.MinThreshold);
            // Counted separately from the listed sample: printing the sample's
            // length told a shop with 40 depleted items that only 6 were low.
            lowStockTotal = await lowQuery.CountAsync(cancellationToken);
            outOfStock = await lowQuery.CountAsync(p => p.Quantity <= 0, cancellationToken);
            stockAlerts = (await lowQuery
                .OrderBy(p => p.Quantity)
                .Select(p => new { p.Name, p.Quantity })
                .Take(LowStockListLimit)
                .ToListAsync(cancellationToken))
                .Select(p => (p.Name, p.Quantity))
                .ToList();
        }

        var topProducts = saleIds.Count == 0
            ? []
            : await _db.SaleItems.AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .GroupBy(si => si.IsExternal ? si.ExternalProductName : (si.Product != null ? si.Product.Name : null))
                .Where(g => g.Key != null)
                .Select(g => new { Name = g.Key!, Qty = g.Sum(x => x.Quantity), Sum = g.Sum(x => x.SalePrice * x.Quantity) })
                .OrderByDescending(x => x.Sum)
                .Take(TopProductLimit)
                .ToListAsync(cancellationToken);

        // ── Render ───────────────────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.Append("<b>📊 ").Append(Escape(market.Name)).Append(" · ")
          .Append(localDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append("</b>\n\n");

        // «Мои продажи» when scoped to one cashier — otherwise the shop total.
        sb.Append(options.SellerId is null ? "<b>Продажи</b>\n" : "<b>Мои продажи</b>\n");
        if (checkCount == 0)
        {
            sb.Append("Продаж за день нет.\n");
        }
        else
        {
            sb.Append("Чеков: ").Append(checkCount).Append('\n');
            sb.Append("Выручка: <b>").Append(Money(revenue)).Append("</b> сум\n");
            sb.Append("Наличные: ").Append(Money(cashIn))
              .Append(" · Карта: ").Append(Money(cardIn))
              .Append(" · В долг: ").Append(Money(debtSold)).Append('\n');
            sb.Append("Средний чек: ").Append(Money(revenue / checkCount)).Append(" сум\n");
            if (options.IncludeProfit)
                sb.Append("Прибыль: <b>").Append(Money(profit)).Append("</b> сум (маржа ")
                  .Append(margin.ToString("0.#", CultureInfo.InvariantCulture)).Append("%)\n");
        }
        if (returns is { Count: > 0 })
            sb.Append("Возвраты: ").Append(Money(returns.Sum)).Append(" сум (").Append(returns.Count).Append(")\n");

        if (options.IncludeCash || options.IncludeDebts)
        {
            sb.Append("\n<b>Касса и долги</b>\n");
            if (options.IncludeCash)
                sb.Append("В кассе сейчас: <b>").Append(Money(cashBalance)).Append("</b> сум\n");
            if (options.IncludeDebts)
            {
                if (debtPaidToday > 0)
                    sb.Append("Погашено долгов за день: ").Append(Money(debtPaidToday)).Append(" сум\n");
                sb.Append("Долги клиентов: ").Append(Money(debtTotal)).Append(" сум\n");
                if (overdue is { Count: > 0 })
                    sb.Append("⚠️ Просрочено: <b>").Append(Money(overdue.Value.Sum)).Append("</b> сум (")
                      .Append(overdue.Value.Count).Append(")\n");
            }
        }

        if (options.IncludeStock)
        {
            sb.Append("\n<b>Склад</b>\n");
            if (lowStockTotal == 0)
            {
                sb.Append("Все товары в достатке.\n");
            }
            else
            {
                if (outOfStock > 0) sb.Append("Закончилось: ").Append(outOfStock).Append('\n');
                sb.Append("Заканчивается: ").Append(lowStockTotal).Append('\n');
                foreach (var p in stockAlerts)
                    sb.Append("• ").Append(Escape(p.Name)).Append(" — ").Append(Money(p.Quantity)).Append('\n');
                if (lowStockTotal > stockAlerts.Count) sb.Append("• …\n");
            }
        }

        if (topProducts.Count > 0)
        {
            sb.Append("\n<b>Топ товаров дня</b>\n");
            var rank = 1;
            foreach (var p in topProducts)
                sb.Append(rank++).Append(". ").Append(Escape(p.Name)).Append(" — ")
                  .Append(Money(p.Sum)).Append(" сум\n");
        }

        return sb.ToString().TrimEnd();
    }

    private static string Money(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>Telegram HTML parse_mode reserves &lt; &gt; &amp; — product and
    /// market names are user-supplied, so they must be escaped or the whole
    /// message fails to send.</summary>
    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
