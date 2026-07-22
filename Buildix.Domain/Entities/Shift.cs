using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// A seller's work session — opened when they start their shift and closed
/// when they finish.
///
/// This is distinct from <see cref="User.ShiftStatus"/>: that is the
/// admin-controlled work *permission* (Active / Blocked / Scheduled), whereas
/// a <see cref="Shift"/> row records the actual time the seller worked. It
/// backs the real <c>shiftDurationMinutes</c> on the seller dashboard.
/// </summary>
public class Shift : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>When the seller opened the shift (UTC).</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>When the seller closed it (UTC). Null while the shift is open.</summary>
    public DateTime? ClosedAt { get; set; }

    // ── Kassa-smena rekonsiliatsiyasi (dizayn Смены ekrani) ──────────────
    /// <summary>Smena boshida kassada bo'lgan naqd ("В кассе на начало").</summary>
    public decimal OpeningCash { get; set; }

    /// <summary>Yopishда kassir sanab kiritgan faktik naqd. Null = hali yopilmagan.</summary>
    public decimal? CountedCash { get; set; }

    /// <summary>Faktik − kutilgan (Расхождение). 0 = сошлась. Yopilганда hisoblanadi.</summary>
    public decimal Discrepancy { get; set; }

    /// <summary>Rekonsiliatsiya holati: Open / Balanced / Discrepancy.</summary>
    public CashShiftStatus ReconStatus { get; set; } = CashShiftStatus.Open;

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    /// <summary>True while the shift has not yet been closed.</summary>
    public bool IsOpen => ClosedAt is null;

    /// <summary>Worked minutes — up to <see cref="ClosedAt"/>, or up to now
    /// while the shift is still open. Clamped to non-negative against clock skew.</summary>
    public int DurationMinutes
    {
        get
        {
            var minutes = ((ClosedAt ?? DateTime.UtcNow) - OpenedAt).TotalMinutes;
            return minutes > 0 ? (int)minutes : 0;
        }
    }
}
