using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Single source of truth for a Sale's charged total. Shared by SaleService
/// (item add/remove, discount, return, cancel paths) and SalePaymentService so
/// the money total is computed exactly one way — a server-side SUM over the
/// sale's items, minus the sale-level discount, clamped at zero.
/// </summary>
internal static class SaleTotals
{
    /// <summary>
    /// Recompute <see cref="Sale.TotalAmount"/> from the authoritative SUM over
    /// its SaleItems (never from a stale in-memory collection), minus
    /// <see cref="Sale.DiscountAmount"/>. Clamped at 0 so an over-large discount
    /// — or a fixed discount left on a sale whose items later shrank — can never
    /// push the total negative and corrupt every report that sums TotalAmount.
    /// Callers must SaveChanges any item add/remove BEFORE calling so the SUM
    /// sees them, and Update/track the sale afterward to persist the new total.
    /// </summary>
    public static async Task RecalculateAsync(
        IAppDbContext context, Sale sale, CancellationToken cancellationToken = default)
    {
        var gross = await context.SaleItems
            .Where(si => si.SaleId == sale.Id)
            .SumAsync(si => si.SalePrice * si.Quantity, cancellationToken);

        sale.TotalAmount = Math.Max(0m, gross - sale.DiscountAmount);
    }
}
