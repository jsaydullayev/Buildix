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

    public async Task<string?> BuildAsync(int marketId, DateTime localDate, CancellationToken cancellationToken = default)
    {
        var market = await _db.Markets.AsNoTracking()
            .Where(m => m.Id == marketId)
            .Select(m => new { m.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (market is null) return null;

        var (dayStart, dayEnd) = _clock.LocalDayToUtcRange(localDate);

        // ── Sales for the day (Draft/Cancelled never count as revenue) ───────
        var daySales = _db.Sales.AsNoTracking().Where(s =>
            s.MarketId == marketId && !s.IsDeleted &&
            s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled &&
            s.CreatedAt >= dayStart && s.CreatedAt < dayEnd);

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
        var debtSold = await _db.Debts.AsNoTracking()
            .Where(d => d.MarketId == marketId && d.CreatedAt >= dayStart && d.CreatedAt < dayEnd)
            .SumAsync(d => (decimal?)d.TotalDebt, cancellationToken) ?? 0m;

        // ── Profit (owner-only bot, so always included) ──────────────────────
        // Effective cost mirrors SaleItem.EffectiveCostPrice: external items
        // carry their cost in ExternalCostPrice while CostPrice stays 0.
        var profit = saleIds.Count == 0
            ? 0m
            : await _db.SaleItems.AsNoTracking()
                .Where(si => saleIds.Contains(si.SaleId))
                .SumAsync(si => (decimal?)((si.SalePrice - (si.IsExternal ? si.ExternalCostPrice : si.CostPrice)) * si.Quantity),
                    cancellationToken) ?? 0m;
        var margin = revenue > 0 ? profit / revenue * 100m : 0m;

        // ── Returns for the day ──────────────────────────────────────────────
        var returns = await _db.SaleReturns.AsNoTracking()
            .Where(r => r.MarketId == marketId && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd)
            // SaleReturn has no soft-delete flag — returns are permanent records.
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Sum = g.Sum(x => x.TotalAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        // ── Cash + debts ─────────────────────────────────────────────────────
        var cashBalance = await _db.CashRegisters.AsNoTracking()
            .Where(c => c.MarketId == marketId)
            .Select(c => (decimal?)c.CurrentBalance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var openDebts = _db.Debts.AsNoTracking()
            .Where(d => d.MarketId == marketId && d.Status == DebtStatus.Open && d.RemainingDebt > 0);
        var debtTotal = await openDebts.SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
        var now = _clock.UtcNow;
        var overdue = await openDebts
            .Where(d => d.DueDate != null && d.DueDate < now)
            .GroupBy(d => 1)
            .Select(g => new { Count = g.Count(), Sum = g.Sum(x => x.RemainingDebt) })
            .FirstOrDefaultAsync(cancellationToken);

        // Debt repayments collected today — payments booked against a sale that
        // carries a debt. Same definition as DebtQueryService.GetSummaryStatsAsync
        // so the bot and the Долги screen never disagree.
        var debtPaidToday = await _db.Payments.AsNoTracking()
            .Where(p => p.Sale != null && p.Sale.MarketId == marketId && p.Sale.Debt != null
                && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // ── Stock signals + top products ─────────────────────────────────────
        var stockAlerts = await _db.Products.AsNoTracking()
            .Where(p => p.MarketId == marketId && !p.IsDeleted && !p.IsHidden && p.Quantity <= p.MinThreshold)
            .OrderBy(p => p.Quantity)
            .Select(p => new { p.Name, p.Quantity })
            .Take(LowStockListLimit + 1)
            .ToListAsync(cancellationToken);
        var outOfStock = stockAlerts.Count(x => x.Quantity <= 0);

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

        sb.Append("<b>Продажи</b>\n");
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
            sb.Append("Прибыль: <b>").Append(Money(profit)).Append("</b> сум (маржа ")
              .Append(margin.ToString("0.#", CultureInfo.InvariantCulture)).Append("%)\n");
        }
        if (returns is { Count: > 0 })
            sb.Append("Возвраты: ").Append(Money(returns.Sum)).Append(" сум (").Append(returns.Count).Append(")\n");

        sb.Append("\n<b>Касса и долги</b>\n");
        sb.Append("В кассе сейчас: <b>").Append(Money(cashBalance)).Append("</b> сум\n");
        if (debtPaidToday > 0)
            sb.Append("Погашено долгов за день: ").Append(Money(debtPaidToday)).Append(" сум\n");
        sb.Append("Долги клиентов: ").Append(Money(debtTotal)).Append(" сум\n");
        if (overdue is { Count: > 0 })
            sb.Append("⚠️ Просрочено: <b>").Append(Money(overdue.Sum)).Append("</b> сум (")
              .Append(overdue.Count).Append(")\n");

        sb.Append("\n<b>Склад</b>\n");
        if (stockAlerts.Count == 0)
        {
            sb.Append("Все товары в достатке.\n");
        }
        else
        {
            if (outOfStock > 0) sb.Append("Закончилось: ").Append(outOfStock).Append('\n');
            sb.Append("Заканчивается: ").Append(stockAlerts.Count).Append('\n');
            foreach (var p in stockAlerts.Take(LowStockListLimit))
                sb.Append("• ").Append(Escape(p.Name)).Append(" — ").Append(Money(p.Quantity)).Append('\n');
            if (stockAlerts.Count > LowStockListLimit) sb.Append("• …\n");
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
