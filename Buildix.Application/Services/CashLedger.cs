using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Application.Services;

/// <inheritdoc cref="ICashLedger"/>
public sealed class CashLedger : ICashLedger
{
    private readonly IAppDbContext _db;

    public CashLedger(IAppDbContext db) => _db = db;

    public void Record(int marketId, decimal amount, CashMovementType type,
        Guid? userId = null, Guid? shiftId = null, int? refNumber = null,
        string? category = null, string? comment = null)
    {
        // 0 summa — harakat yo'q.
        if (amount == 0m) return;

        _db.CashMovements.Add(new CashMovement
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            ShiftId = shiftId,
            Type = type,
            Amount = amount,
            Category = category,
            RefNumber = refNumber,
            Comment = comment,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
