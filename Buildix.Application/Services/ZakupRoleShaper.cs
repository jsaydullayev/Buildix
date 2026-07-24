using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;

namespace Buildix.Application.Services;

/// <summary>
/// Cost-visibility shaping of zakup / goods-receipt DTOs, moved out of
/// ZakupsController. A caller WITHOUT data.costPrice (canViewCost == false) gets
/// the cost-free "seller" DTO variants (ZakupSellerDto / ZakupReceiptSellerDto);
/// everyone else gets the full DTO. (See <see cref="IZakupRoleShaper"/>.)
/// </summary>
public sealed class ZakupRoleShaper : IZakupRoleShaper
{
    private static ZakupSellerDto ToSeller(ZakupDto z) =>
        new(z.Id, z.ProductId, z.ProductName, z.Quantity, z.CreatedAt, z.CreatedBy);

    private static ZakupReceiptSellerDto ToSeller(ZakupReceiptDto r) => new(
        r.Id, r.SupplierId, r.SupplierName, r.InvoiceNumber, r.DeliveryStatus, r.ItemCount, r.CreatedAt, r.CreatedBy,
        r.Items.Select(i => new ZakupReceiptLineSellerDto(i.Id, i.ProductId, i.ProductName, i.Quantity)).ToList());

    public object ShapeZakup(ZakupDto zakup, bool canViewCost) =>
        canViewCost ? zakup : ToSeller(zakup);

    public IEnumerable<object> ShapeZakups(IEnumerable<ZakupDto> zakups, bool canViewCost) =>
        canViewCost ? zakups.Cast<object>() : zakups.Select(z => (object)ToSeller(z));

    public object ShapeZakupsPaged(PagedResult<ZakupDto> paged, bool canViewCost) =>
        canViewCost
            ? paged
            : PagedResult<ZakupSellerDto>.From(paged.Items.Select(ToSeller).ToList(), paged.Page, paged.Size, paged.Total);

    public object ShapeReceipt(ZakupReceiptDto receipt, bool canViewCost) =>
        canViewCost ? receipt : ToSeller(receipt);

    public IEnumerable<object> ShapeReceipts(IEnumerable<ZakupReceiptDto> receipts, bool canViewCost) =>
        canViewCost ? receipts.Cast<object>() : receipts.Select(r => (object)ToSeller(r));

    public object ShapeReceiptsPaged(PagedResult<ZakupReceiptDto> paged, bool canViewCost) =>
        canViewCost
            ? paged
            : PagedResult<ZakupReceiptSellerDto>.From(paged.Items.Select(ToSeller).ToList(), paged.Page, paged.Size, paged.Total);
}
