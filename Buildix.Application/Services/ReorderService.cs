using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

public class ReorderService : IReorderService
{
    private const int WindowDays = 30; // sotuv tezligini o'lchash oynasi
    private const int TargetCoverDays = 14; // tavsiya: shuncha kunga zaxira

    private readonly IAppDbContext _db;
    private readonly ICurrentMarketService _currentMarket;

    public ReorderService(IAppDbContext db, ICurrentMarketService currentMarket)
    {
        _db = db;
        _currentMarket = currentMarket;
    }

    public async Task<IReadOnlyList<ReorderSuggestionDto>> GetSuggestionsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var cutoff = DateTime.UtcNow.AddDays(-WindowDays);

        // Products for this market (non-deleted via global filter).
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.MarketId == marketId)
            .Select(p => new { p.Id, p.Name, p.Quantity, p.MinThreshold, p.Unit })
            .ToListAsync(cancellationToken);

        // Units sold per product over the window (exclude drafts/cancelled).
        var sold = await _db.SaleItems
            .AsNoTracking()
            .Where(si => si.ProductId != null
                && si.Sale.MarketId == marketId
                && si.Sale.CreatedAt >= cutoff
                && si.Sale.Status != SaleStatus.Draft
                && si.Sale.Status != SaleStatus.Cancelled)
            .GroupBy(si => si.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.ProductId!.Value, x => x.Qty, cancellationToken);

        var suggestions = new List<ReorderSuggestionDto>();
        foreach (var p in products)
        {
            var soldQty = sold.TryGetValue(p.Id, out var q) ? q : 0m;
            var avgDaily = soldQty / WindowDays;
            var isLow = p.Quantity <= p.MinThreshold;
            int? daysOfCover = avgDaily > 0 ? (int)Math.Floor(p.Quantity / avgDaily) : null;

            // Faqat kam qolgan yoki 7 kundan kam zaxirasi bor tovarlar.
            if (!isLow && !(daysOfCover.HasValue && daysOfCover.Value < 7))
                continue;

            decimal suggested;
            if (avgDaily > 0)
                suggested = Math.Max(0m, Math.Ceiling(TargetCoverDays * avgDaily - p.Quantity));
            else
                suggested = Math.Max(p.MinThreshold, p.MinThreshold * 2 - p.Quantity); // sotuv yo'q, kam qolgan

            if (suggested <= 0) continue;

            suggestions.Add(new ReorderSuggestionDto(
                p.Id,
                p.Name,
                UnitName(p.Unit),
                p.Quantity,
                p.MinThreshold,
                Math.Round(avgDaily, 2),
                daysOfCover,
                suggested));
        }

        // Eng shoshilinchi (kam kun) birinchi; sotuv yo'q kam-qoldiqlar oxirida.
        return suggestions
            .OrderBy(s => s.DaysOfCover ?? int.MaxValue)
            .ThenByDescending(s => s.SuggestedQty)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    private static string UnitName(UnitType unit) => unit switch
    {
        UnitType.Piece => "dona",
        UnitType.Kilogram => "kg",
        UnitType.Meter => "m",
        UnitType.Bag => "qop",
        UnitType.Ton => "t",
        UnitType.Sheet => "list",
        UnitType.Bucket => "chelak",
        UnitType.Roll => "rulon",
        UnitType.Box => "quti",
        UnitType.Pack => "pachka",
        UnitType.Liter => "l",
        _ => "",
    };
}
