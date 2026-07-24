using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Ombor harakati jurnali — bitta tovar qoldig'i har o'zgarganda bir qator.
/// Faqat qo'shiladigan (append-only) ledger: qoldiq QANDAY shu holatga
/// kelganini ko'rsatadi (kelish/sotuv/tuzatish), va Склад ekranidagi
/// "Движение товара" oynasini to'ldiradi.
///
/// Har yozuv o'zgarish sodir bo'lgan operatsiyaning AYNAN o'sha tranzaksiyasi
/// ichida yoziladi — shuning uchun qoldiq bilan ledger hech qachon farq qilmaydi.
/// </summary>
public class StockMovement : BaseEntity
{
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Qoldiq o'zgarishi (± ). Sotuv manfiy, kelish/tuzatish musbat bo'lishi mumkin.</summary>
    public decimal Delta { get; set; }

    /// <summary>Harakatdan KEYINGI qoldiq — "стало: 84 меш." qatori uchun.</summary>
    public decimal ResultingQty { get; set; }

    /// <summary>
    /// Manba hujjat raqami: Purchase → ZakupReceipt.ReceiptNumber (З-###),
    /// Sale → Sale.SaleNumber (Ч-####). Correction/InitialStock uchun null.
    /// </summary>
    public int? RefNumber { get; set; }

    /// <summary>Kim yaratdi (JWT actor). Tizim/hook uchun null bo'lishi mumkin.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string? Comment { get; set; }
}
