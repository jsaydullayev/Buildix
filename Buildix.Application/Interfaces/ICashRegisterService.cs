using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

public interface ICashRegisterService
{
    Task<CashRegisterDto?> GetCashRegisterAsync(CancellationToken cancellationToken = default);
    Task<Result<bool>> WithdrawCashAsync(WithdrawCashRequest request, Guid userId, CancellationToken cancellationToken = default);

    // ── Naqd yechish tasdiq oqimi (MarketSettings.CashWithdrawalNeedsApproval) ──
    /// <summary>Admin so'rov yuboradi — Pending yozuv, balansga TEGMAYDI. Egaga Telegram xabar.</summary>
    Task<Result<bool>> RequestWithdrawalAsync(WithdrawCashRequest request, Guid requestedByUserId, CancellationToken cancellationToken = default);
    /// <summary>Egasi tasdiqlaydi — Approved + balansdan yechiladi (yetarli mablag' tekshiruvi).</summary>
    Task<Result<bool>> ApproveWithdrawalAsync(Guid withdrawalId, Guid approverUserId, CancellationToken cancellationToken = default);
    /// <summary>Egasi rad etadi — Rejected, balans o'zgarmaydi.</summary>
    Task<Result<bool>> RejectWithdrawalAsync(Guid withdrawalId, Guid approverUserId, CancellationToken cancellationToken = default);
    /// <summary>Yechish so'rovlari ro'yxati (status bo'yicha filtr; null = hammasi).</summary>
    Task<IReadOnlyList<WithdrawalListItemDto>> GetWithdrawalsAsync(string? status, CancellationToken cancellationToken = default);
    /// <summary>
    /// Add cash to the till. Y3 — <paramref name="userId"/> is the JWT-extracted
    /// caller identity (the controller pulls it from ClaimTypes.NameIdentifier).
    /// Logged as the actor on the resulting audit row so deposits are as
    /// accountable as withdrawals are.
    /// </summary>
    Task<bool> AddCashAsync(decimal amount, Guid userId, CancellationToken cancellationToken = default);
    Task<TodaySalesSummaryDto?> GetTodaySalesSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Касса kunlik ledger'i (balans + приход/расход + tiplangan harakatlar ro'yxati).</summary>
    Task<CashLedgerDto> GetCashLedgerAsync(DateTime? localDate, CancellationToken cancellationToken = default);
}
