namespace Buildix.Domain.Enums;

/// <summary>
/// Qaytarish sababi — dizayndagi «Брак» / «Не подошёл» / «Ошибка продавца».
/// Persisted as <c>integer</c>; raqamlar DB kontrakti.
/// </summary>
public enum ReturnReason
{
    /// <summary>Брак — nuqsonli tovar.</summary>
    Defect = 0,

    /// <summary>Не подошёл — mijozga to'g'ri kelmadi.</summary>
    NotFit = 1,

    /// <summary>Ошибка продавца — sotuvchi xatosi.</summary>
    SellerError = 2,

    /// <summary>Boshqa sabab.</summary>
    Other = 3,
}
