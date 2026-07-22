using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

public record DebtDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleId")] Guid SaleId,
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("customerName")] string? CustomerName,
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("remainingDebt")] decimal RemainingDebt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("dueDate")] DateTime? DueDate,
    [property: JsonPropertyName("saleItems")] List<SaleItemDto>? SaleItems
);

public record DebtorDto(
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("customerName")] string? CustomerName,
    [property: JsonPropertyName("customerPhone")] string? CustomerPhone,
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("paidAmount")] decimal PaidAmount,
    [property: JsonPropertyName("remainingDebt")] decimal RemainingDebt,
    [property: JsonPropertyName("debtCount")] int DebtCount,
    [property: JsonPropertyName("oldestDebtDate")] DateTime? OldestDebtDate,
    [property: JsonPropertyName("sales")] List<SaleDto> Sales
);

public record PayDebtDto(
    [property: JsonPropertyName("amount")][param: System.ComponentModel.DataAnnotations.Range(0.01, double.MaxValue, ErrorMessage = "To'lov summasi 0 dan katta bo'lishi kerak")] decimal Amount,
    [property: JsonPropertyName("paymentType")][param: System.ComponentModel.DataAnnotations.Required] string PaymentType
);

public record PayDebtResultDto(
    decimal RemainingDebt,
    decimal PaymentAmount,
    string DebtStatus
);

/// <summary>Долги ekranidagi bitta qator: mijoz bo'yicha jamlangan qarz.</summary>
public record DebtorSummaryDto(
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("customerName")] string? CustomerName,
    [property: JsonPropertyName("customerPhone")] string CustomerPhone,
    [property: JsonPropertyName("customerType")] string CustomerType,
    [property: JsonPropertyName("remainingDebt")] decimal RemainingDebt,
    [property: JsonPropertyName("debtCount")] int DebtCount,
    [property: JsonPropertyName("nearestDueDate")] DateTime? NearestDueDate,
    [property: JsonPropertyName("isOverdue")] bool IsOverdue
);

/// <summary>Долги ekrani sarlavhasidagi stat kartalar.</summary>
public record DebtSummaryStatsDto(
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("overdueTotal")] decimal OverdueTotal,
    [property: JsonPropertyName("overdueCount")] int OverdueCount,
    [property: JsonPropertyName("customerCount")] int CustomerCount,
    [property: JsonPropertyName("paidToday")] decimal PaidToday,
    [property: JsonPropertyName("paidThisMonth")] decimal PaidThisMonth
);

/// <summary>
/// Qarzning to'lov muddatini (due date) yangilash tanasi. Null yuborilsa —
/// muddat olib tashlanadi.
/// </summary>
public record UpdateDebtDueDateDto(
    [property: JsonPropertyName("dueDate")] DateTime? DueDate
);
