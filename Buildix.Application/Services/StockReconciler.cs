using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <inheritdoc cref="IStockReconciler"/>
public sealed class StockReconciler : IStockReconciler
{
    private readonly IAppDbContext _db;

    public StockReconciler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<StockDrift>> FindDriftAsync(
        int marketId, CancellationToken ct = default)
    {
        // Uchta guruhli so'rov — tovar soniga bog'liq emas. Har tovar uchun
        // alohida so'rov yuborish minglik katalogda panelni to'xtatib
        // qo'yardi.
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.MarketId == marketId && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name, p.Quantity })
            .ToListAsync(ct);

        if (products.Count == 0) return [];

        var ledger = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.MarketId == marketId)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(m => m.Delta) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Total, ct);

        // Ochiq qoralamalar ushlab turgan miqdor. Tashqi (IsExternal)
        // qatorlar omborga umuman tegmaydi, shuning uchun chiqariladi.
        var reserved = await _db.SaleItems
            .AsNoTracking()
            .Where(si => si.ProductId != null
                && !si.IsExternal
                && si.Sale != null
                && si.Sale.MarketId == marketId
                && si.Sale.Status == SaleStatus.Draft)
            .GroupBy(si => si.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(si => si.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Total, ct);

        var drifts = new List<StockDrift>();
        foreach (var p in products)
        {
            var fromLedger = ledger.GetValueOrDefault(p.Id);
            var held = reserved.GetValueOrDefault(p.Id);
            var drift = p.Quantity - (fromLedger - held);

            if (drift != 0m)
                drifts.Add(new StockDrift(p.Id, p.Name, p.Quantity, fromLedger, held, drift));
        }

        // Eng katta farq birinchi — egasi qaysi tovardan boshlashini ko'rsin.
        return drifts.OrderByDescending(d => Math.Abs(d.Drift)).ToList();
    }
}
