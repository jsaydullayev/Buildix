using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// A goods-receipt header (priyomka) — one delivery from a supplier that may
/// contain several product line items (<see cref="Zakup"/>). Groups the lines,
/// carries the supplier/invoice reference and the payment state toward the
/// supplier, so a single delivery of many products is recorded as one document
/// rather than N disconnected purchases.
/// </summary>
public class ZakupReceipt : BaseEntity
{
    /// <summary>
    /// Per-market ketma-ket priyomka raqami (dizaynda «№214»). Yaratilганда
    /// market bo'yicha max+1 beriladi. 0 = eski yozuvlar. InvoiceNumber esa
    /// yetkazuvchining raqami — bu ichki, tartibli raqam.
    /// </summary>
    public int ReceiptNumber { get; set; }

    /// <summary>Optional — a quick re-stock may have no named supplier.</summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Supplier's invoice / nakladnoy number, free text.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>Sum of every line's Quantity * CostPrice.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>How much of <see cref="TotalAmount"/> has been paid so far.</summary>
    public decimal PaidAmount { get; set; }

    /// <summary>Derived from Paid vs Total; stored for cheap balance queries.</summary>
    public SupplierPaymentStatus PaymentStatus { get; set; } = SupplierPaymentStatus.Unpaid;

    /// <summary>«Способ оплаты» — to'langan summa qanday berilgani ("Cash" | "Transfer").
    /// Ma'lumot uchun (Новый закуп). Null = ko'rsatilmagan / eski yozuv.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Yetkazish holati (В пути / Принят). Default <see cref="DeliveryStatus.Accepted"/>
    /// — eski yozuvlar va standart oqim (yaratilishi = qabul, darhol stok).
    /// InTransit bo'lsa stok faqat <c>accept</c> da qo'shiladi.
    /// </summary>
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Accepted;

    // ── Yetkazish maʼlumotlari (В пути kartochkasi — Поставки ekrani) ────────
    /// <summary>Haydovchi telefoni ("водитель: +998...") — InTransit uchun.</summary>
    public string? DriverPhone { get; set; }
    /// <summary>Kutilayotgan yetib kelish vaqti (ETA). O'tib ketsa «задерживается».</summary>
    public DateTime? ExpectedDate { get; set; }

    public string? Comment { get; set; }

    public Guid CreatedByAdminId { get; set; }
    public User CreatedByAdmin { get; set; } = null!;

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Optimistic concurrency (PostgreSQL system column xmin) — guards concurrent
    // supplier-payment updates from clobbering each other's PaidAmount/status.
    public uint Xmin { get; set; }

    // Navigation properties — the product lines received in this delivery.
    public ICollection<Zakup> Items { get; set; } = new List<Zakup>();

    /// <summary>Outstanding amount still owed to the supplier for this receipt.</summary>
    public decimal OutstandingAmount => TotalAmount - PaidAmount;
}
