using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

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
}
