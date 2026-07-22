namespace Buildix.Domain.Enums;

/// <summary>
/// Naqd yechish so'rovining tasdiq holati (dizayn: "Снятие наличных — с одобрения
/// владельца"). MarketSettings.CashWithdrawalNeedsApproval yoqilganda administrator
/// so'rov yuboradi, egasi tasdiqlaydi/rad etadi.
/// </summary>
public enum WithdrawalApprovalStatus
{
    /// <summary>Egasi tasdig'i talab qilinmagan holda darhol bajarilgan (eski xatti-harakat).</summary>
    NotRequired = 0,

    /// <summary>So'rov yuborilgan, egasi tasdig'ini kutmoqda.</summary>
    Pending = 1,

    /// <summary>Egasi tasdiqladi — naqd yechildi.</summary>
    Approved = 2,

    /// <summary>Egasi rad etdi.</summary>
    Rejected = 3,
}
