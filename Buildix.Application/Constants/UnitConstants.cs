namespace Buildix.Application.Constants;

/// <summary>
/// O'lchov birliklari konstantlari
/// Flutter va Web frontend uchun
/// </summary>
public static class UnitConstants
{
    /// <summary>
    /// Barcha o'lchov birliklari ro'yxati
    /// </summary>
    public static readonly List<UnitInfo> AllUnits = new()
    {
        new UnitInfo(1, "dona", "Piece", "шт"),
        new UnitInfo(2, "kg", "Kilogram", "кг"),
        new UnitInfo(3, "m", "Meter", "м"),
        new UnitInfo(4, "qop", "Bag", "меш."),
        new UnitInfo(5, "t", "Ton", "т"),
        new UnitInfo(6, "list", "Sheet", "лист."),
        new UnitInfo(7, "chelak", "Bucket", "вед."),
        new UnitInfo(8, "rulon", "Roll", "рулон"),
        new UnitInfo(9, "quti", "Box", "коробка"),
        new UnitInfo(10, "pachka", "Pack", "пачка"),
        new UnitInfo(11, "l", "Liter", "л")
    };
}

/// <summary>
/// O'lchov birligi ma'lumotlari
/// </summary>
public record UnitInfo(
    int Value,
    string NameUz,      // O'zbekcha: dona, kg, m
    string NameEn,      // Inglizcha: Piece, Kilogram, Meter
    string NameRu       // Ruscha: Дона, Килограмм, Метр
);
