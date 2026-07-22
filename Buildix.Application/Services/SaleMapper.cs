using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Sale → DTO mapping, shared by SaleService (writes) and SaleQueryService
/// (reads) so a Sale is projected to a <see cref="SaleDto"/> exactly one way.
/// Two flavours:
///   • <see cref="MapSale"/> — synchronous, for entities already loaded with
///     their Seller / Customer / SaleItems.Product / Payments navigations
///     (the read paths use eager Include).
///   • <see cref="MapToDtoAsync"/> — for a bare Sale, resolving items, product
///     names, payments, seller and customer via the repositories (the write
///     paths return a freshly-mutated Sale that isn't fully graph-loaded).
/// </summary>
internal static class SaleMapper
{
    public static SaleItemDto MapItem(SaleItem item, string productName, string unit = "")
    {
        // Effective cost: external lines carry their own cost, ordinary lines
        // use the product cost captured on the line.
        decimal effectiveCostPrice = item.IsExternal
            ? item.ExternalCostPrice
            : item.CostPrice;

        return new SaleItemDto(
            item.Id.ToString(),
            item.SaleId.ToString(),
            item.ProductId,
            productName,
            item.Quantity,
            effectiveCostPrice,
            item.SalePrice,
            item.TotalPrice,
            (item.SalePrice - effectiveCostPrice) * item.Quantity,
            unit,
            item.Comment,
            item.IsExternal
        );
    }

    public static SaleDto MapSale(Sale s) => new(
        s.Id,
        s.SaleNumber,
        s.SellerId,
        s.Seller?.FullName ?? "Unknown",
        s.CustomerId,
        s.Customer?.FullName,
        s.Customer?.Phone,
        s.Status.ToString(),
        s.TotalAmount,
        s.PaidAmount,
        s.TotalAmount - s.PaidAmount,
        s.DiscountAmount,
        s.CreatedAt,
        s.SaleItems.Select(si =>
        {
            string productName;
            string unit = "";
            if (!si.IsExternal)
            {
                productName = si.Product?.Name ?? "Unknown";
                unit = si.Product?.GetUnitName() ?? "";
            }
            else
            {
                productName = si.ExternalProductName ?? "Tashqi mahsulot";
            }
            return MapItem(si, productName, unit);
        }).ToList(),
        s.Payments.Select(p => new PaymentDto(
            p.Id,
            p.PaymentType.ToString().ToLowerInvariant(),
            p.Amount,
            p.CreatedAt,
            null,
            null,
            null
        )).ToList()
    );

    public static async Task<SaleDto> MapToDtoAsync(
        Sale sale, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var saleItems = await unitOfWork.SaleItems.FindAsync(si => si.SaleId == sale.Id, cancellationToken);
        var itemsDto = new List<SaleItemDto>();

        // Batch fetch ordinary products to avoid N+1 (external lines carry their
        // own name and need no lookup).
        var ordinaryProductIds = saleItems
            .Where(si => !si.IsExternal && si.ProductId.HasValue)
            .Select(si => si.ProductId!.Value)
            .Distinct()
            .ToList();

        var products = new Dictionary<Guid, Product>();
        if (ordinaryProductIds.Any())
        {
            var productList = await unitOfWork.Products.FindAsync(
                p => ordinaryProductIds.Contains(p.Id) && p.MarketId == sale.MarketId,
                cancellationToken);
            foreach (var p in productList)
            {
                products[p.Id] = p;
            }
        }

        foreach (var item in saleItems)
        {
            string? productName = null;
            string unit = "";

            if (!item.IsExternal)
            {
                // ProductId is nullable on the entity; guard before .Value so a
                // corrupt row degrades to "Unknown" instead of throwing.
                if (item.ProductId.HasValue &&
                    products.TryGetValue(item.ProductId.Value, out var product))
                {
                    productName = product.Name;
                    unit = product.GetUnitName();
                }
            }
            else
            {
                productName = item.ExternalProductName;
            }

            itemsDto.Add(MapItem(item, productName ?? "Unknown", unit));
        }

        var payments = await unitOfWork.Payments.FindAsync(p => p.SaleId == sale.Id, cancellationToken);
        var paymentsDto = payments.Select(p => new PaymentDto(
            p.Id,
            p.PaymentType.ToString().ToLowerInvariant(),
            p.Amount,
            p.CreatedAt,
            null,
            null,
            null
        )).ToList();

        var seller = await unitOfWork.Users.GetByIdAsync(sale.SellerId, cancellationToken);
        var customer = sale.CustomerId.HasValue
            ? await unitOfWork.Customers.GetByIdAsync(sale.CustomerId.Value, cancellationToken)
            : null;

        return new SaleDto(
            sale.Id,
            sale.SaleNumber,
            sale.SellerId,
            seller?.FullName ?? "Unknown",
            sale.CustomerId,
            customer?.FullName,
            customer?.Phone,
            sale.Status.ToString(),
            sale.TotalAmount,
            sale.PaidAmount,
            sale.TotalAmount - sale.PaidAmount,
            sale.DiscountAmount,
            sale.CreatedAt,
            itemsDto,
            paymentsDto
        );
    }
}
