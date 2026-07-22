using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Applies cost-visibility shaping to zakup / goods-receipt DTOs: a caller WITHOUT
/// data.costPrice (canViewCost == false) receives the cost-free "seller" variants
/// (ZakupSellerDto / ZakupReceiptSellerDto); everyone else gets the full DTO.
/// Returns <see cref="object"/> so the two shapes can be serialised by their
/// runtime type. This moves the projection + gating out of the controller.
/// </summary>
public interface IZakupRoleShaper
{
    object ShapeZakup(ZakupDto zakup, bool canViewCost);
    IEnumerable<object> ShapeZakups(IEnumerable<ZakupDto> zakups, bool canViewCost);
    object ShapeZakupsPaged(PagedResult<ZakupDto> paged, bool canViewCost);

    object ShapeReceipt(ZakupReceiptDto receipt, bool canViewCost);
    IEnumerable<object> ShapeReceipts(IEnumerable<ZakupReceiptDto> receipts, bool canViewCost);
    object ShapeReceiptsPaged(PagedResult<ZakupReceiptDto> paged, bool canViewCost);
}
