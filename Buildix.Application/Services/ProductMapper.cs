using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Services;

/// <summary>
/// Product → DTO mapping, shared by ProductQueryService (reads), ProductService
/// (create/update read-back) and ProductImageService so a Product projects one
/// way. <paramref name="canViewCost"/> masks the cost price for roles without
/// the data.costPrice permission.
/// </summary>
internal static class ProductMapper
{
    public static ProductDto MapToDto(Product product, bool canViewCost = true)
    {
        return new ProductDto(
            product.Id,
            product.Name,
            canViewCost ? product.CostPrice : 0m,
            product.SalePrice,
            product.MinSalePrice,
            product.Quantity,
            product.MinThreshold,
            (int)product.Unit,  // Cast enum to int
            product.GetUnitName(),  // Unit name (dona/kg/m)
            product.CategoryId,
            product.Category?.Name,
            product.IsTemporary,
            product.IsInStock(1),  // Simplified check
            product.IsLowStock,
            product.ImageUrl,
            product.HidePriceFromSellers,
            product.Sku,
            product.Description,
            product.IsHidden,
            product.WarehouseLocation
        );
    }
}
