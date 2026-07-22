namespace Buildix.Domain.Enums;

/// <summary>
/// O'lchov birliklari turi
/// </summary>
public enum UnitType
{
    /// <summary>
    /// Dona (piece) - butun sonlar uchun
    /// Masalan: 5 dona telefon, 10 dona stul
    /// </summary>
    Piece = 1,

    /// <summary>
    /// Kilogram - og'irlik o'lchovi
    /// Masalan: 1.5 kg olma, 0.5 kg shakar
    /// </summary>
    Kilogram = 2,

    /// <summary>
    /// Metr - uzunlik o'lchovi
    /// Masalan: 2.5 metr mato, 10.5 metr sim
    /// </summary>
    Meter = 3,

    // ── Qurilish do'koni birliklari (dizaynda: меш./т/лист./вед.) ─────────
    /// <summary>Qop / мешок — masalan sement, gips.</summary>
    Bag = 4,
    /// <summary>Tonna / тонна — masalan armatura, qum.</summary>
    Ton = 5,
    /// <summary>List / лист — masalan profnastil, shifer.</summary>
    Sheet = 6,
    /// <summary>Chelak / ведро — masalan bo'yoq.</summary>
    Bucket = 7,
    /// <summary>Rulon / рулон.</summary>
    Roll = 8,
    /// <summary>Quti / коробка.</summary>
    Box = 9,
    /// <summary>Pachka / пачка.</summary>
    Pack = 10,
    /// <summary>Litr / литр.</summary>
    Liter = 11
}
