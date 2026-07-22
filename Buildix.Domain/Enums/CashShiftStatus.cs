namespace Buildix.Domain.Enums;

/// <summary>
/// Kassa-smenasi rekonsiliatsiya holati (dizayn Смены ekrani: Открыта / Сошлась /
/// Расхождение). <see cref="ShiftStatus"/> dan farqli — u kassirning ish-ruxsati,
/// bu esa smena yopilгандаги naqd hisob-kitob natijasi.
/// </summary>
public enum CashShiftStatus
{
    /// <summary>Smena ochiq — hali yopilmagan.</summary>
    Open = 0,

    /// <summary>Yopildi, faktik naqd kutilgan bilan mos keldi (Сошлась).</summary>
    Balanced = 1,

    /// <summary>Yopildi, faktik naqd kutilgandan farq qildi (Расхождение).</summary>
    Discrepancy = 2,
}
