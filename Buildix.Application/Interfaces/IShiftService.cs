using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Manages seller work sessions (<c>Shift</c>). Each user has at most one open
/// shift at a time; all operations are scoped to the current market.
/// </summary>
public interface IShiftService
{
    /// <summary>The user's currently open shift, or null when none is open.</summary>
    Task<ShiftDto?> GetCurrentShiftAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Opens a work shift for the user. Idempotent — if a shift is
    /// already open it is returned unchanged.</summary>
    Task<ShiftDto> OpenShiftAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Closes the user's open shift, optionally reconciling the drawer
    /// against <paramref name="countedCash"/> (faktik sanalgan naqd). Throws
    /// <see cref="InvalidOperationException"/> when no shift is open.</summary>
    Task<ShiftDto> CloseShiftAsync(Guid userId, decimal? countedCash = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-close ANOTHER cashier's open shift, by shift id, on behalf of
    /// <paramref name="closedByUserId"/> (the Owner/Admin). Used when a seller
    /// leaves without closing — otherwise the shift stays open forever and
    /// breaks the "sales only with an open shift" rule and cash reconciliation.
    /// The audit row records who forced it. Throws
    /// <see cref="InvalidOperationException"/> when the shift is missing or
    /// already closed.
    /// </summary>
    Task<ShiftDto> ForceCloseShiftAsync(Guid shiftId, Guid closedByUserId, decimal? countedCash = null, CancellationToken cancellationToken = default);

    /// <summary>Market-wide shift history (all cashiers), most recent first —
    /// for the Смены history table (Owner/Admin). Optional server-side filters:
    /// <paramref name="userId"/> (one cashier) and a UTC [from, to) range.</summary>
    Task<IReadOnlyList<ShiftDto>> GetMarketShiftsAsync(int limit = 30, Guid? userId = null, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default);

    /// <summary>The worked-shift sessions of <paramref name="userId"/> in the
    /// current market, most recent first (capped at <paramref name="limit"/>).
    /// Lets an Owner/Admin review how long a seller actually worked; market-scoped
    /// so it never leaks shifts from another tenant.</summary>
    Task<IReadOnlyList<ShiftDto>> GetUserShiftsAsync(Guid userId, int limit = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// The CALLER's own shift history for a period plus its totals — the seller
    /// Смены screen. Self-service (no users.shift permission, which sellers do
    /// not hold); <paramref name="range"/> is week (default) | month | all,
    /// anchored to Tashkent business days.
    /// </summary>
    Task<MyShiftsDto> GetMyShiftsAsync(Guid userId, string? range = null, CancellationToken cancellationToken = default);
}
