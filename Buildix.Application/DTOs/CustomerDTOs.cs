using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

public record CustomerDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("fullName")] string? FullName,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("customerType")] string CustomerType = "Individual",
    [property: JsonPropertyName("isRegular")] bool IsRegular = false,
    [property: JsonPropertyName("debtLimit")] decimal? DebtLimit = null,
    // Seller Клиенты ekrani — «Покупок за месяц» (joriy oy: soni+summa) va
    // «Последняя» (oxirgi xarid sanasi). Ro'yxatда sahifadagi mijozlar uchun
    // hisoblanadi (TotalDebt kabi), null = xarid yo'q.
    [property: JsonPropertyName("monthPurchaseCount")] int MonthPurchaseCount = 0,
    [property: JsonPropertyName("monthPurchaseSum")] decimal MonthPurchaseSum = 0,
    [property: JsonPropertyName("lastPurchaseAt")] DateTime? LastPurchaseAt = null
);

public record CreateCustomerDto(
    [property: JsonPropertyName("phone")]
    [param: Required(ErrorMessage = "Telefon raqam majburiy")]
    [param: RegularExpression(@"^\+?[0-9]{9,15}$", ErrorMessage = "Telefon raqam formati noto'g'ri")]
    string Phone,

    [property: JsonPropertyName("fullName")]
    [param: StringLength(100, ErrorMessage = "Ism 100 belgidan oshmasligi kerak")]
    string? FullName,

    [property: JsonPropertyName("comment")]
    [param: StringLength(500, ErrorMessage = "Izoh 500 belgidan oshmasligi kerak")]
    string? Comment,

    [property: JsonPropertyName("initialDebt")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Boshlang'ich qarz manfiy bo'lishi mumkin emas")]
    decimal? InitialDebt,

    [property: JsonPropertyName("customerType")] string? CustomerType = null,
    [property: JsonPropertyName("isRegular")] bool IsRegular = false,
    [property: JsonPropertyName("debtLimit")]
    [param: Range(0, double.MaxValue)]
    decimal? DebtLimit = null
);

/// <summary>
/// <see cref="Id"/> is the preferred lookup key. Existing clients that only
/// know the phone (legacy callers) can still send <see cref="Phone"/> with a
/// null/empty <see cref="Id"/>; the service falls back to a per-market phone
/// lookup in that case.
/// </summary>
public record UpdateCustomerDto(
    [property: JsonPropertyName("id")] Guid? Id,

    [property: JsonPropertyName("phone")]
    [param: RegularExpression(@"^\+?[0-9]{9,15}$", ErrorMessage = "Telefon raqam formati noto'g'ri")]
    string? Phone,

    [property: JsonPropertyName("fullName")]
    [param: StringLength(100, ErrorMessage = "Ism 100 belgidan oshmasligi kerak")]
    string? FullName,

    [property: JsonPropertyName("customerType")] string? CustomerType = null,
    [property: JsonPropertyName("isRegular")] bool? IsRegular = null,
    [property: JsonPropertyName("debtLimit")]
    [param: Range(0, double.MaxValue)]
    decimal? DebtLimit = null
);

public record CustomerDeleteInfoDto(
    [property: JsonPropertyName("canDelete")] bool CanDelete,
    [property: JsonPropertyName("salesCount")] int SalesCount,
    [property: JsonPropertyName("draftSalesCount")] int DraftSalesCount,
    [property: JsonPropertyName("debtsCount")] int DebtsCount,
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("warningMessage")] string? WarningMessage
);
