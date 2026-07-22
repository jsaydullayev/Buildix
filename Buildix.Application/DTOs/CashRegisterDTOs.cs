using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

// Response DTOs use `record` with init-only properties so the existing
// object-initializer call sites (CashRegisterService) keep working while the
// type is immutable — consistent with the rest of the DTO layer.

public record CashRegisterDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("currentBalance")]
    public decimal CurrentBalance { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; init; }

    [JsonPropertyName("withdrawals")]
    public List<CashWithdrawalDto> Withdrawals { get; init; } = new();
}

public record CashWithdrawalDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    [JsonPropertyName("withdrawalDate")]
    public DateTime WithdrawalDate { get; init; }

    [JsonPropertyName("userName")]
    public string? UserName { get; init; }

    [JsonPropertyName("withdrawType")]
    public string WithdrawType { get; init; } = "cash"; // 'cash' or 'click'
}

/// <summary>Одобрение наличных — bitta yechish so'rovi (approval oqimi qatori).</summary>
public record WithdrawalListItemDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    [JsonPropertyName("withdrawType")]
    public string WithdrawType { get; init; } = "cash";

    /// <summary>NotRequired | Pending | Approved | Rejected.</summary>
    [JsonPropertyName("approvalStatus")]
    public string ApprovalStatus { get; init; } = "NotRequired";

    [JsonPropertyName("requestedByName")]
    public string? RequestedByName { get; init; }

    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; init; }

    [JsonPropertyName("approvedByName")]
    public string? ApprovedByName { get; init; }

    [JsonPropertyName("approvedAt")]
    public DateTime? ApprovedAt { get; init; }
}

public record AddCashRequest
{
    [JsonPropertyName("amount")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Summa 0 dan katta bo'lishi kerak")]
    public decimal Amount { get; init; }
}

public record WithdrawCashRequest
{
    [JsonPropertyName("amount")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Summa 0 dan katta bo'lishi kerak")]
    public decimal Amount { get; init; }

    [JsonPropertyName("comment")]
    public string Comment { get; init; } = string.Empty;

    [JsonPropertyName("withdrawType")]
    public string WithdrawType { get; init; } = "cash"; // 'cash' or 'click'
}

public record TodaySalesSummaryDto
{
    [JsonPropertyName("totalSales")]
    public int TotalSales { get; init; }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; init; }

    [JsonPropertyName("totalPaid")]
    public decimal TotalPaid { get; init; }

    [JsonPropertyName("debtAmount")]
    public decimal DebtAmount { get; init; }

    [JsonPropertyName("cashPaid")]
    public decimal CashPaid { get; init; }

    [JsonPropertyName("cardPaid")]
    public decimal CardPaid { get; init; }

    [JsonPropertyName("clickPaid")]
    public decimal ClickPaid { get; init; }

    [JsonPropertyName("date")]
    public DateTime Date { get; init; }
}

public record CashBalanceDto(
    [property: JsonPropertyName("cashInRegister")] decimal CashInRegister,
    [property: JsonPropertyName("cardPayments")] decimal CardPayments,
    [property: JsonPropertyName("totalBalance")] decimal TotalBalance
);
