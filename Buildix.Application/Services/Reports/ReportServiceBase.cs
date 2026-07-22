using Buildix.Application.Interfaces;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Shared base for the report/dashboard services. Holds the Tashkent clock and
/// the Tashkent-anchored UTC date-range helpers that every report service needs,
/// so the conversion logic lives in exactly one place (DRY) instead of being
/// copy-pasted across each focused service.
/// </summary>
public abstract class ReportServiceBase(ITashkentClock clock)
{
    protected readonly ITashkentClock _clock = clock;

    /// <summary>
    /// Start (UTC) of the Tashkent calendar day that <paramref name="date"/> falls in.
    /// Used by report PDFs/Excel for human-readable date labels — anchored to Tashkent.
    /// </summary>
    protected DateTime ToUtcDate(DateTime date) => _clock.LocalDayToUtcRange(date).UtcStart;

    /// <summary>
    /// Convert a Tashkent-local calendar date to the UTC half-open range covering that day.
    /// Tashkent is GMT+5: a "2026-05-12 daily report" must include sales from
    /// 2026-05-11 19:00 UTC to 2026-05-12 19:00 UTC.
    /// </summary>
    protected (DateTime Start, DateTime End) GetUtcDateRange(DateTime date) =>
        _clock.LocalDayToUtcRange(date);
}
