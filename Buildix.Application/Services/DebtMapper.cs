using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Extensions;

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
                // Nom — SOTUV paytidagi nusxadan (`DisplayName` tashqi
                // tovarni ham, oddiy tovarni ham qamrab oladi). Ilgari u
                // jonli `Product` dan olinardi va tovar o'chirilgach mijoz
                // olgan mollar qarz ekranida «Noma'lum mahsulot» bo'lib
                // qolardi — qarzni undirish paytida esa aynan shu ro'yxat
                // kerak bo'ladi.
                si.DisplayName is { Length: > 0 } name ? name : "Noma'lum mahsulot",
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
                si.IsExternal ? "" : si.ProductUnit.GetUnitName(),
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
