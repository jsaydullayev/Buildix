using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid SaleId { get; set; }
    public PaymentType PaymentType { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// The cashier who physically took this money, when that is NOT the sale's
    /// own seller — i.e. debt collected later, possibly by a different person on
    /// a different day. Shift reconciliation used to attribute every payment via
    /// <c>Sale.SellerId</c>, so cash collected by cashier B on cashier A's sale
    /// landed in A's drawer figures while sitting in B's till: a phantom
    /// shortage at B's close and an already-reconciled A.
    ///
    /// NULL means "the sale's seller collected it" — the normal at-checkout case
    /// and every row written before this column existed, so the fallback keeps
    /// historical shifts computing exactly as before.
    /// </summary>
    public Guid? CollectedByUserId { get; set; }
    public User? CollectedByUser { get; set; }

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Navigation properties
    public Sale Sale { get; set; } = null!;
}
