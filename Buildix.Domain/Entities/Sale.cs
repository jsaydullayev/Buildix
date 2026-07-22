using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class Sale : BaseEntity, ISoftDelete
{
    /// <summary>
    /// Per-market ketma-ket chek raqami (dizaynda «ЧЕК №1046»). Draft yaratilganda
    /// beriladi (market bo'yicha max+1). 0 = hali berilmagan (eski yozuvlar).
    /// Guid Id tashqi ko'rsatishga yaroqsiz bo'lgani uchun qo'shildi.
    /// </summary>
    public int SaleNumber { get; set; }

    public Guid SellerId { get; set; }
    public Guid? CustomerId { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }

    // Sale-level discount (skidka), in currency. Subtracted from the gross item
    // sum when computing the charged TotalAmount — see
    // SaleService.RecalculateSaleTotalAsync. Item SalePrices are left untouched,
    // so per-item history and the invoice line items stay intact; only the bill
    // total drops. 0 = no discount.
    public decimal DiscountAmount { get; set; }

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // Optimistic concurrency token. Mapped to PostgreSQL's built-in xmin column
    // so concurrent payment / cancellation writes detect each other instead of
    // silently overwriting Status (Paid vs Debt vs Cancelled).
    public uint Xmin { get; set; }

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Navigation properties
    public User Seller { get; set; } = null!;
    public Customer? Customer { get; set; }
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public Debt? Debt { get; set; }
}
