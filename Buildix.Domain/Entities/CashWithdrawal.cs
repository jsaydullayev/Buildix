using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class CashWithdrawal : BaseEntity
{
    public decimal Amount { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime WithdrawalDate { get; set; }
    public string WithdrawType { get; set; } = "cash"; // 'cash' or 'click'

    // ── Egasi tasdig'i oqimi (dizayn: "Снятие наличных — с одобрения владельца") ──
    /// <summary>Tasdiq holati. NotRequired = eski/darhol yechish.</summary>
    public WithdrawalApprovalStatus ApprovalStatus { get; set; } = WithdrawalApprovalStatus.NotRequired;

    /// <summary>So'rovni yuborgan (odatda administrator).</summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>Tasdiqlagan/rad etgan egasi.</summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>Tasdiqlangan/rad etilgan vaqt (UTC).</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Qaysi kassa-smenaga tegishli (rekonsiliatsiya uchun).</summary>
    public Guid? ShiftId { get; set; }

    /// <summary>
    /// Tenant scope. Always set from <c>ICurrentMarketService</c> when the row
    /// is created — never inferred from <see cref="UserId"/> at read time,
    /// otherwise orphan rows (UserId=null after a user is hard-deleted) leak
    /// across tenants. See K2 in the security audit.
    /// </summary>
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    public Guid? UserId { get; set; } // Kim olgani
    public User? User { get; set; }
}
