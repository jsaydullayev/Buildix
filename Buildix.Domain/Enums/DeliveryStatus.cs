namespace Buildix.Domain.Enums;

/// <summary>
/// Xarid (priyomka) yetkazish holati — dizayndagi «В пути» / «Принят».
/// Persisted as <c>integer</c>; raqamlar DB kontrakti.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>
    /// Qabul qilingan — tovar omborga kirgan, qoldiq va tannarx yangilangan.
    /// Eski yozuvlar va standart (backward-compat) holat: yaratilishi = qabul.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// Yo'lda — hujjat yaratilgan, lekin tovar hali omborga kirmagan. Qabul
    /// qilinganda (<c>accept</c>) stok qo'shiladi.
    /// </summary>
    InTransit = 1,
}
