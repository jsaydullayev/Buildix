using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <inheritdoc cref="IStockLedger"/>
public sealed class StockLedger : IStockLedger
{
    private readonly IAppDbContext _db;

    public StockLedger(IAppDbContext db) => _db = db;

    public void Record(Product product, decimal delta, StockMovementType type,
        int? refNumber = null, Guid? userId = null, string? comment = null)
    {
        // Delta 0 bo'lsa harakat yo'q — bo'sh qator yozmaymiz.
        if (delta == 0m) return;

        _db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(),
            MarketId = product.MarketId,
            ProductId = product.Id,
            Type = type,
            Delta = delta,
            // Chaqiruvchi Quantity'ni allaqachon yangilagan — harakatdan keyingi holat.
            ResultingQty = product.Quantity,
            RefNumber = refNumber,
            UserId = userId,
            Comment = comment,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public async Task RecordSaleFinalizationAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        // Liniyalarni o'zimiz o'qiymiz — chaqiruvchi SaleItems'ni yuklaganiga
        // bog'lanmaymiz. Tashqi (IsExternal) va productId'siz liniyalar chiqadi.
        var items = await _db.SaleItems
            .Where(si => si.SaleId == sale.Id && !si.IsExternal && si.ProductId != null)
            .ToListAsync(cancellationToken);
        if (items.Count == 0) return;

        var ids = items.Select(si => si.ProductId!.Value).Distinct().ToList();
        // Tovarlarning JORIY qoldig'i (savat qurishda kamayishi allaqachon
        // saqlangan) — ResultingQty aynan shundan olinadi.
        var products = await _db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var si in items)
            if (products.TryGetValue(si.ProductId!.Value, out var product))
                Record(product, -si.Quantity, StockMovementType.Sale,
                    refNumber: sale.SaleNumber, userId: sale.SellerId);
    }
}
