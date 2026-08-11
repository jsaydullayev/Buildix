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
    [property: JsonPropertyName("expectedCash")] decimal ExpectedCash = 0,
    // Per-tender breakdown for the Смены screen. CashIn/CardIn above stay NET of
    // refunds (they drive ExpectedCash); the counts below are receipts, and
    // returns are reported separately rather than folded into a tender.
    [property: JsonPropertyName("debtIn")] decimal DebtIn = 0,
    [property: JsonPropertyName("cashCount")] int CashCount = 0,
    [property: JsonPropertyName("cardCount")] int CardCount = 0,
    [property: JsonPropertyName("debtCount")] int DebtCount = 0,
    [property: JsonPropertyName("returnAmount")] decimal ReturnAmount = 0,
    [property: JsonPropertyName("returnCount")] int ReturnCount = 0,
    /// <summary>Per-market sequential number printed on the receipt ("Смена №112").</summary>
    [property: JsonPropertyName("shiftNumber")] int ShiftNumber = 0,
    // Cashless split. CardIn above stays the FULL cashless total so existing
    // clients keep reconciling; these break it down: TerminalIn + ClickIn == CardIn.
    // The counts are receipts and may overlap — one mixed receipt can hold both.
    [property: JsonPropertyName("terminalIn")] decimal TerminalIn = 0,
    [property: JsonPropertyName("clickIn")] decimal ClickIn = 0,
    [property: JsonPropertyName("terminalCount")] int TerminalCount = 0,
    [property: JsonPropertyName("clickCount")] int ClickCount = 0,
    /// <summary>
    /// Qo'shni do'kondan olingan tovarlar uchun kassadan berilgan pul. Chiqim,
    /// shuning uchun ExpectedCash'dan AYIRILGAN. Alohida ko'rsatiladi — aks holda
    /// kassir kutilayotgan naqd nega kamayganini tushunmaydi.
    /// </summary>
    [property: JsonPropertyName("externalPayouts")] decimal ExternalPayouts = 0
);

/// <summary>
/// A seller's own shift history for a period, plus the period totals the
/// Смены screen shows under the table ("Итого за неделю").
/// </summary>
public record MyShiftsDto(
    [property: JsonPropertyName("items")] IReadOnlyList<ShiftDto> Items,
    [property: JsonPropertyName("totalRevenue")] decimal TotalRevenue,
    [property: JsonPropertyName("totalChecks")] int TotalChecks,
    [property: JsonPropertyName("avgCheck")] decimal AvgCheck
);

/// <summary>Smenani yopish tanasi — kassir faktik sanagan naqd.</summary>
public record CloseShiftRequest(
    [property: JsonPropertyName("countedCash")] decimal? CountedCash = null
);

/// <summary>
/// Посещаемость — davomat hisoboti (dizayn Смены → Посещаемость tab). Smena
/// ochilish/yopilish vaqtlaridan hisoblanadi; alohida jadval yo'q.
/// </summary>
public record AttendanceDto(
    /// <summary>So'ralgan davr: "week" | "month".</summary>
    [property: JsonPropertyName("period")] string Period,
    /// <summary>Do'kon ish grafigi (dizayn: 08:00–20:00).</summary>
    [property: JsonPropertyName("scheduleFrom")] string ScheduleFrom,
    [property: JsonPropertyName("scheduleTo")] string ScheduleTo,
    /// <summary>Kechikish chegarasi (dizayn: 08:15).</summary>
    [property: JsonPropertyName("lateAfter")] string LateAfter,
    /// <summary>Davrga rejalashtirilgan jami soat (kunlar × grafik soati). % shu asosda.</summary>
    [property: JsonPropertyName("planHours")] decimal PlanHours,
    [property: JsonPropertyName("items")] IReadOnlyList<AttendanceRowDto> Items
);

/// <summary>Bitta xodimning davr bo'yicha davomati.</summary>
public record AttendanceRowDto(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("shiftCount")] int ShiftCount,
    [property: JsonPropertyName("dayCount")] int DayCount,
    [property: JsonPropertyName("totalHours")] decimal TotalHours,
    [property: JsonPropertyName("avgShiftHours")] decimal AvgShiftHours,
    [property: JsonPropertyName("lateCount")] int LateCount
);
