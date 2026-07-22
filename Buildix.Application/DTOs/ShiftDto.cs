using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>A cash-shift session — see <c>Buildix.Domain.Entities.Shift</c>.</summary>
public record ShiftDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("cashierName")] string CashierName,
    [property: JsonPropertyName("openedAt")] DateTime OpenedAt,
    [property: JsonPropertyName("closedAt")] DateTime? ClosedAt,
    [property: JsonPropertyName("isOpen")] bool IsOpen,
    [property: JsonPropertyName("durationMinutes")] int DurationMinutes,
    // Kassa rekonsiliatsiyasi
    [property: JsonPropertyName("openingCash")] decimal OpeningCash,
    [property: JsonPropertyName("countedCash")] decimal? CountedCash,
    [property: JsonPropertyName("discrepancy")] decimal Discrepancy,
    [property: JsonPropertyName("reconStatus")] string ReconStatus,
    // Смена davomidagi savdo (list uchun; joriy smenada ham to'ldiriladi)
    [property: JsonPropertyName("checkCount")] int CheckCount = 0,
    [property: JsonPropertyName("revenue")] decimal Revenue = 0,
    [property: JsonPropertyName("cashIn")] decimal CashIn = 0,
    [property: JsonPropertyName("cardIn")] decimal CardIn = 0,
    [property: JsonPropertyName("withdrawals")] decimal Withdrawals = 0,
    [property: JsonPropertyName("expectedCash")] decimal ExpectedCash = 0
);

/// <summary>Smenani yopish tanasi — kassir faktik sanagan naqd.</summary>
public record CloseShiftRequest(
    [property: JsonPropertyName("countedCash")] decimal? CountedCash = null
);
