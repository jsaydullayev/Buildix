namespace Buildix.Domain.Enums;

/// <summary>
/// Ombor harakati turi (StockMovement). Persisted as <c>integer</c>, shuning
/// uchun raqamlar DB kontrakti — qayta tartiblamang, faqat oxiriga qo'shing.
/// </summary>
public enum StockMovementType
{
    /// <summary>Tovar yaratilganda kiritilgan boshlang'ich qoldiq.</summary>
    InitialStock = 0,

    /// <summary>Xarid (zakup) qabul qilinganda kelgan tovar — "Приход · З-###".</summary>
    Purchase = 1,

    /// <summary>Sotuv natijasida chiqqan tovar — "Продажа · Ч-####".</summary>
    Sale = 2,

    /// <summary>Sotuvni bekor qilish/qaytarish — qoldiqqa qaytdi.</summary>
    SaleReversal = 3,

    /// <summary>Inventarizatsiya yoki qo'lda tuzatish — "Корректировка".</summary>
    Correction = 4,
}
