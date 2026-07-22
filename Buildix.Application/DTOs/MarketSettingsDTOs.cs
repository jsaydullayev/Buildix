using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Full read/write view of a market's Настройки. `shiftAutoCloseTime` is
/// "HH:mm" or null; `defaultLanguage` is "ru"/"uz". One DTO backs both the
/// GET response and the "Сохранить изменения" whole-form PUT.
/// </summary>
public record MarketSettingsDto(
    // Магазин
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("workingHours")] string? WorkingHours,
    // Касса и смены
    [property: JsonPropertyName("salesOnlyWhenShiftOpen")] bool SalesOnlyWhenShiftOpen,
    [property: JsonPropertyName("cashWithdrawalNeedsApproval")] bool CashWithdrawalNeedsApproval,
    [property: JsonPropertyName("debtOnlyForRegulars")] bool DebtOnlyForRegulars,
    [property: JsonPropertyName("defaultDebtLimit")] decimal DefaultDebtLimit,
    [property: JsonPropertyName("allowedCashDiscrepancy")] decimal AllowedCashDiscrepancy,
    [property: JsonPropertyName("shiftAutoCloseTime")] string? ShiftAutoCloseTime,
    // Чек
    [property: JsonPropertyName("receiptHeader")] string? ReceiptHeader,
    [property: JsonPropertyName("receiptFooter")] string? ReceiptFooter,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt,
    // Локаль
    [property: JsonPropertyName("defaultLanguage")] string DefaultLanguage,
    [property: JsonPropertyName("firstDayOfWeek")] int FirstDayOfWeek,
    // Склад и цены
    [property: JsonPropertyName("minStockAlertEnabled")] bool MinStockAlertEnabled,
    [property: JsonPropertyName("blockSaleBelowCost")] bool BlockSaleBelowCost,
    [property: JsonPropertyName("defaultMarkupPct")] decimal DefaultMarkupPct,
    // Уведомления
    [property: JsonPropertyName("notifyDaySummary")] bool NotifyDaySummary,
    [property: JsonPropertyName("notifyOverdueDebts")] bool NotifyOverdueDebts,
    [property: JsonPropertyName("notifyWithdrawalRequests")] bool NotifyWithdrawalRequests,
    [property: JsonPropertyName("ownerTelegram")] string? OwnerTelegram,
    [property: JsonPropertyName("ownerTelegramLinked")] bool OwnerTelegramLinked,
    // Безопасность
    [property: JsonPropertyName("inactivityLogoutMinutes")] int InactivityLogoutMinutes,
    [property: JsonPropertyName("auditEnabled")] bool AuditEnabled
);

/// <summary>Whole-form settings update ("Сохранить изменения").</summary>
public record UpdateMarketSettingsRequest(
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("workingHours")] string? WorkingHours,
    [property: JsonPropertyName("salesOnlyWhenShiftOpen")] bool SalesOnlyWhenShiftOpen,
    [property: JsonPropertyName("cashWithdrawalNeedsApproval")] bool CashWithdrawalNeedsApproval,
    [property: JsonPropertyName("debtOnlyForRegulars")] bool DebtOnlyForRegulars,
    [property: JsonPropertyName("defaultDebtLimit")] decimal DefaultDebtLimit,
    [property: JsonPropertyName("allowedCashDiscrepancy")] decimal AllowedCashDiscrepancy,
    [property: JsonPropertyName("shiftAutoCloseTime")] string? ShiftAutoCloseTime,
    [property: JsonPropertyName("receiptHeader")] string? ReceiptHeader,
    [property: JsonPropertyName("receiptFooter")] string? ReceiptFooter,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt,
    [property: JsonPropertyName("defaultLanguage")] string DefaultLanguage,
    [property: JsonPropertyName("firstDayOfWeek")] int FirstDayOfWeek,
    [property: JsonPropertyName("minStockAlertEnabled")] bool MinStockAlertEnabled,
    [property: JsonPropertyName("blockSaleBelowCost")] bool BlockSaleBelowCost,
    [property: JsonPropertyName("defaultMarkupPct")] decimal DefaultMarkupPct,
    [property: JsonPropertyName("notifyDaySummary")] bool NotifyDaySummary,
    [property: JsonPropertyName("notifyOverdueDebts")] bool NotifyOverdueDebts,
    [property: JsonPropertyName("notifyWithdrawalRequests")] bool NotifyWithdrawalRequests,
    [property: JsonPropertyName("ownerTelegram")] string? OwnerTelegram,
    [property: JsonPropertyName("inactivityLogoutMinutes")] int InactivityLogoutMinutes,
    [property: JsonPropertyName("auditEnabled")] bool AuditEnabled
);
