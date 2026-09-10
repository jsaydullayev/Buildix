using Buildix.Domain.Enums;

namespace Buildix.Domain.Extensions;

public static class UnitTypeExtensions
{
    /// <summary>
    /// O'lchov birligining qisqartmasi ("dona", "qop", "m").
    /// </summary>
    /// <remarks>
    /// <para><b>Nega <see cref="Entities.Product"/> dan chiqarildi.</b> Sotuv
    /// qatori endi birlikni O'ZIDA saqlaydi (sotuv paytidagi nusxa), ya'ni
    /// qisqartmani mahsulotsiz ham chiqarish kerak bo'ladi — tovar
    /// o'chirilgan bo'lsa ham chekda «2 qop» yozilishi shart.
    /// <c>Product.GetUnitName()</c> endi shu yerga murojaat qiladi, ya'ni
    /// qisqartmalar bitta joyda turadi: ikki nusxa bo'lsa, biriga yangi
    /// birlik qo'shilib ikkinchisiga qo'shilmay qolardi.</para>
    /// </remarks>
    public static string GetUnitName(this UnitType unit) => unit switch
    {
        UnitType.Piece => "dona",
        UnitType.Kilogram => "kg",
        UnitType.Meter => "m",
        UnitType.Bag => "qop",
        UnitType.Ton => "t",
        UnitType.Sheet => "list",
        UnitType.Bucket => "chelak",
        UnitType.Roll => "rulon",
        UnitType.Box => "quti",
        UnitType.Pack => "pachka",
        UnitType.Liter => "l",
        _ => "noma'lum"
    };
}
