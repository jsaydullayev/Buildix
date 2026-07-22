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

    /// <summary>Market-wide shift history (all cashiers), most recent first —
    /// for the Смены history table (Owner/Admin).</summary>
    Task<IReadOnlyList<ShiftDto>> GetMarketShiftsAsync(int limit = 30, CancellationToken cancellationToken = default);

    /// <summary>The worked-shift sessions of <paramref name="userId"/> in the
    /// current market, most recent first (capped at <paramref name="limit"/>).
    /// Lets an Owner/Admin review how long a seller actually worked; market-scoped
    /// so it never leaks shifts from another tenant.</summary>
    Task<IReadOnlyList<ShiftDto>> GetUserShiftsAsync(Guid userId, int limit = 30, CancellationToken cancellationToken = default);
}
