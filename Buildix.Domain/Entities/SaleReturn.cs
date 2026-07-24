using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Qaytarish hujjati (В-##) — bitta sotuvdan bir yoki bir necha tovar liniyasi
/// qaytarilganda yaratiladi. Dizayndagi «Возвраты» ekranidagi bir qator.
///
/// Hujjat sabab (Reason) va pul qaytarish usulini (RefundMethod) qamrab oladi;
/// stok qaytarilishi + naqd chiqimi + savdo summasining kamayishi shu bilan bir
/// tranzaksiyada bajariladi (SaleReversalService.CreateReturnAsync).
/// </summary>
public class SaleReturn : BaseEntity
{
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    /// <summary>Per-market ketma-ket raqam (В-##).</summary>
    public int Number { get; set; }

    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }

    public ReturnReason Reason { get; set; }

    /// <summary>Pul qaytarish usuli: Cash (Наличные) / Terminal (На карту) / Transfer (Перечисление).</summary>
    public PaymentType RefundMethod { get; set; }

    /// <summary>Qaytarilgan umumiy summa (musbat).</summary>
    public decimal TotalAmount { get; set; }

    public string? Comment { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public ICollection<SaleReturnItem> Items { get; set; } = new List<SaleReturnItem>();
}

/// <summary>Qaytarish hujjatining bitta liniyasi.</summary>
public class SaleReturnItem : BaseEntity
{
    public Guid SaleReturnId { get; set; }
    public SaleReturn? SaleReturn { get; set; }

    /// <summary>Qaysi sotuv liniyasi (agar hali mavjud bo'lsa). To'liq qaytarishda liniya o'chishi mumkin.</summary>
    public Guid? SaleItemId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>Tovar nomi snapshot — mahsulot keyin o'zgarsa ham hujjat o'zgarmaydi.</summary>
    public string ProductName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
