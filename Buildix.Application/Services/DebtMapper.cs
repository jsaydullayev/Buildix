using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Services;

/// <summary>
/// Debt → DTO mapping, shared by DebtQueryService (reads) and DebtService
/// (UpdateDueDate read-back) so a Debt projects to a DebtDto one way.
/// </summary>
internal static class DebtMapper
{
    public static DebtDto MapToDto(Debt debt)
    {
        List<SaleItemDto>? saleItems = null;
        if (debt.Sale?.SaleItems != null)
        {
            saleItems = debt.Sale.SaleItems.Select(si => new SaleItemDto(
                si.Id.ToString(),
                si.SaleId.ToString(),
                si.ProductId,
                // External items have no Product row — fall back to the
                // captured ExternalProductName. Without this they all
                // showed up as "Noma'lum mahsulot" in the debt details
                // screen, hiding which goods the customer actually took.
                si.IsExternal
                    ? (si.ExternalProductName ?? "Noma'lum mahsulot")
                    : (si.Product?.Name ?? "Noma'lum mahsulot"),
                si.Quantity,
                // Effective cost: external items store their cost in
                // ExternalCostPrice (CostPrice stays 0). Profit is computed
                // inline from the same effective cost so the two columns
                // stay consistent — SaleItem.Profit would instead read the
                // *current* Product.CostPrice, drifting from this snapshot.
                si.IsExternal ? si.ExternalCostPrice : si.CostPrice,
                si.SalePrice,
                si.TotalPrice,
                (si.SalePrice - (si.IsExternal ? si.ExternalCostPrice : si.CostPrice)) * si.Quantity,
                si.IsExternal ? "" : (si.Product?.GetUnitName() ?? "dona"),
                si.Comment,
                si.IsExternal
            )).ToList();
        }

        return new DebtDto(
            debt.Id,
            debt.SaleId,
            debt.CustomerId,
            debt.Customer?.FullName,
            debt.TotalDebt,
            debt.RemainingDebt,
            debt.Status.ToString(),
            debt.Sale?.CreatedAt ?? DateTime.MinValue,
            debt.DueDate,
            saleItems
        );
    }
}
