using System.ComponentModel.DataAnnotations;
using Buildix.Domain.Enums;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

public record ReturnSaleItemRequest(string SaleItemId, decimal Quantity, string? Comment);

public record SaleItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("saleId")] string SaleId,
    [property: JsonPropertyName("productId")] Guid? ProductId,
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("costPrice")] decimal CostPrice,
    [property: JsonPropertyName("salePrice")] decimal SalePrice,
    [property: JsonPropertyName("totalPrice")] decimal TotalPrice,
    [property: JsonPropertyName("profit")] decimal Profit,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("isExternal")] bool IsExternal,
    /// <summary>
    /// The unit as its <c>UnitType</c> number. <see cref="Unit"/> is a fixed
    /// Uzbek abbreviation ("dona"/"kg"), which reads wrong in a Russian or
    /// English UI — clients localise from this value instead. 0 = unknown
    /// (external line, or a row from before this field existed).
    /// </summary>
    [property: JsonPropertyName("unitValue")] int UnitValue = 0
);

/// <summary>One tender in a split ("Микс") checkout.</summary>
public record CheckoutTenderDto(
    [property: JsonPropertyName("paymentType")] string PaymentType,
    [property: JsonPropertyName("amount")] decimal Amount
);

/// <summary>
/// Close a sale with one or more tenders in a single transaction. Needed because
/// a split cannot be expressed as two AddPayment calls: the first partial tender
/// is rejected on a walk-in sale (no customer ⇒ cannot leave a debt) and, with a
/// customer, transiently flips the sale to Debt between the two calls.
/// </summary>
public record CheckoutSaleDto(
    [property: JsonPropertyName("tenders")] IReadOnlyList<CheckoutTenderDto> Tenders,
    [property: JsonPropertyName("dueDate")] DateTime? DueDate = null
);

public record PaymentDto(
    [property: JsonPropertyName("paymentId")] Guid PaymentId,
    [property: JsonPropertyName("paymentType")] string PaymentType,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("saleStatus")] string? SaleStatus,
    [property: JsonPropertyName("salePaidAmount")] decimal? SalePaidAmount,
    [property: JsonPropertyName("saleTotalAmount")] decimal? SaleTotalAmount,
    /// <summary>
    /// Who physically took the money, when that is not the sale's own seller —
    /// a debt paid off later can be collected by a different cashier. Null on
    /// the ordinary at-checkout case (and on write paths, which do not load it).
    /// </summary>
    [property: JsonPropertyName("collectedByName")] string? CollectedByName = null
);

public record SaleDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleNumber")] int SaleNumber,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("sellerName")] string SellerName,
    [property: JsonPropertyName("customerId")] Guid? CustomerId,
    [property: JsonPropertyName("customerName")] string? CustomerName,
    [property: JsonPropertyName("customerPhone")] string? CustomerPhone,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("paidAmount")] decimal PaidAmount,
    [property: JsonPropertyName("remainingAmount")] decimal RemainingAmount,
    [property: JsonPropertyName("discountAmount")] decimal DiscountAmount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("items")] List<SaleItemDto> Items,
    [property: JsonPropertyName("payments")] List<PaymentDto> Payments,
    /// <summary>
    /// The shift this receipt belongs to ("Смена №112"). 0 when the sale predates
    /// <c>Sale.ShiftId</c> or the caller did not load the navigation.
    /// </summary>
    [property: JsonPropertyName("shiftNumber")] int ShiftNumber = 0,

    /// <summary>
    /// Chek qaysi KASSADA urilgani («A», «B»). Sotuvchi bu savolga javob
    /// bermaydi: bitta kassir ikkala kassada ham ishlashi mumkin.
    /// <c>null</c> — belgisiz kassa yoki brauzerdan kirilgan.
    /// </summary>
    [property: JsonPropertyName("registerCode")] string? RegisterCode = null
);

public record CreateSaleDto(
    [property: JsonPropertyName("customerId")] Guid? CustomerId
);

public record UpdateSaleCustomerDto(
    [property: JsonPropertyName("customerId")] Guid? CustomerId
);

public record AddSaleItemDto(
    [property: JsonPropertyName("isExternal")] bool IsExternal,
    [property: JsonPropertyName("productId")] Guid? ProductId,

    [property: JsonPropertyName("externalProductName")]
    [param: StringLength(200, ErrorMessage = "Mahsulot nomi 200 belgidan oshmasligi kerak")]
    string? ExternalProductName,

    [property: JsonPropertyName("externalCostPrice")]
    [param: Range(0, double.MaxValue)]
    decimal? ExternalCostPrice,

    [property: JsonPropertyName("quantity")]
    [param: Range(0.001, double.MaxValue, ErrorMessage = "Miqdor 0 dan katta bo'lishi kerak")]
    decimal Quantity,

    [property: JsonPropertyName("salePrice")]
    [param: Range(0, double.MaxValue)]
    decimal SalePrice,

    [property: JsonPropertyName("minSalePrice")]
    [param: Range(0, double.MaxValue)]
    decimal MinSalePrice,

    [property: JsonPropertyName("comment")]
    [param: StringLength(500)]
    string? Comment
);

public record RemoveSaleItemDto(
    [property: JsonPropertyName("saleItemId")]
    [param: Required]
    string SaleItemId,

    [property: JsonPropertyName("quantity")]
    [param: Range(0.001, double.MaxValue, ErrorMessage = "Miqdor 0 dan katta bo'lishi kerak")]
    decimal Quantity
);

/// <summary>
/// Chek qatoriga ANIQ miqdor qo'yish (o'sish emas, o'rnatish). Kassa uchun:
/// kassir "12 qop" yoki "3.5 m" ni bir marta yozadi, 12 marta «+» bosmaydi.
/// 0 — qatorni butunlay o'chiradi (tovar omborga qaytadi).
/// </summary>
public record SetSaleItemQuantityDto(
    [property: JsonPropertyName("saleItemId")]
    [param: Required]
    string SaleItemId,

    [property: JsonPropertyName("quantity")]
    [param: Range(0, 9_999_999, ErrorMessage = "Miqdor 0 dan kichik bo'lmasin")]
    decimal Quantity
);

public record AddPaymentDto(
    [property: JsonPropertyName("paymentType")]
    [param: Required(ErrorMessage = "To'lov turi majburiy")]
    string PaymentType,

    // NOL ataylab ruxsat etilgan: u «to'lanadigan narsa yo'q, chekni yop»
    // degani va butun summasi chegirmaga ketgan chek uchun kerak. Manfiy
    // summa esa hech qachon o'rinli emas.
    //
    // Nol haqiqatan o'rinlimi — buni SalePaymentService hal qiladi: qoldiq
    // bor bo'lsa u rad etiladi. Bu tekshiruv shu qarorni QABUL QILA
    // olmasdi, chunki u chekning holatini ko'rmaydi.
    [property: JsonPropertyName("amount")]
    [param: Range(0, double.MaxValue, ErrorMessage = "To'lov miqdori manfiy bo'lmasin")]
    decimal Amount,

    // Qisman to'lov qarz qoldirsa — yaratilgan qarzning to'lov muddati
    // (ixtiyoriy). To'liq to'lovlarda e'tiborga olinmaydi.
    [property: JsonPropertyName("dueDate")] DateTime? DueDate = null
);

/// <summary>
/// "Qarzga olish" (to'liq qarz) uchun ixtiyoriy to'lov muddati (due date).
/// </summary>
public record MarkSaleAsDebtDto(
    [property: JsonPropertyName("dueDate")] DateTime? DueDate = null
);

public record UpdateSaleItemPriceDto(
    [property: JsonPropertyName("saleItemId")]
    [param: Required]
    string SaleItemId,

    [property: JsonPropertyName("newPrice")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Narx manfiy bo'lishi mumkin emas")]
    decimal NewPrice,

    [property: JsonPropertyName("comment")]
    [param: StringLength(500)]
    string? Comment
);

/// <summary>
/// Sale-level chegirma (skidka) — kassa to'lov oynasida qo'llaniladi. Item
/// narxlariga tegmaydi; faqat sotuvning umumiy hisobini (TotalAmount) kamaytiradi.
/// </summary>
public record SetSaleDiscountDto(
    [property: JsonPropertyName("discountAmount")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Chegirma manfiy bo'lmasin")]
    decimal DiscountAmount
);
