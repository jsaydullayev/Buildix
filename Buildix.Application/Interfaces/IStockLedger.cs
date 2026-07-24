using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Ombor harakati jurnaliga (StockMovement) yozuv qo'shadi. Yozuv chaqiruvchining
/// DbContext'iga QO'SHILADI, lekin SAQLANMAYDI — chaqiruvchi o'z SaveChanges'ida
/// (o'z tranzaksiyasi ichida) saqlaydi. Shu tarzda harakat qoldiq o'zgarishi
/// bilan atomik yoziladi: ikkalasi birga commit bo'ladi yoki ikkalasi ham yo'q.
/// </summary>
public interface IStockLedger
{
    /// <summary>
    /// Bitta harakatni qayd etadi. Chaqiruvchi <c>product.Quantity</c>ni ALLAQACHON
    /// o'zgartirgan bo'lishi kerak — ResultingQty aynan shundan olinadi.
    /// </summary>
    /// <param name="product">Qoldig'i allaqachon yangilangan tovar.</param>
    /// <param name="delta">Qoldiq o'zgarishi (± ).</param>
    /// <param name="type">Harakat turi.</param>
    /// <param name="refNumber">Manba hujjat raqami (Ч-#### / З-###), ixtiyoriy.</param>
    /// <param name="userId">JWT actor, ixtiyoriy.</param>
    /// <param name="comment">Izoh (masalan inventarizatsiya farqi), ixtiyoriy.</param>
    void Record(Product product, decimal delta, StockMovementType type,
        int? refNumber = null, Guid? userId = null, string? comment = null);

    /// <summary>
    /// Sotuv Draft'dan chiqib yakunlanganda (Paid/Debt) har bir tovar liniyasi
    /// uchun bitta <see cref="StockMovementType.Sale"/> harakati yozadi
    /// (delta = −miqdor, ref = ЧЕК №). Stok allaqachon savat qurish paytida
    /// kamaygan — bu yerda faqat QAYD etiladi (draft churn'i jurnalga tushmaydi).
    /// Tashqi (IsExternal) liniyalar e'tiborga olinmaydi. Yozuvlar chaqiruvchi
    /// SaveChanges'ida saqlanadi.
    /// </summary>
    Task RecordSaleFinalizationAsync(Sale sale, CancellationToken cancellationToken = default);
}
