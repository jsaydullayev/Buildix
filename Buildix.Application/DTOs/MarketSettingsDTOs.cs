using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Full read/write view of a market's Настройки. `shiftAutoCloseTime` is
/// "HH:mm" or null; `defaultLanguage` is "ru"/"uz". One DTO backs both the
/// GET response and the "Сохранить изменения" whole-form PUT.
/// </summary>
/// <summary>
/// Kassa uchun chop etish sozlamalari — chek eni va avtomatik chop etish.
/// To'liq sozlamalar ekrani egaga tegishli, bu esa har bir xodimga ochiq.
/// </summary>
public record PosPrintSettingsDto(
    [property: JsonPropertyName("receiptWidthMm")] int ReceiptWidthMm,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt);

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
    // Посещаемость (davomat rejasi) — "HH:mm"
    [property: JsonPropertyName("workDayStart")] string WorkDayStart,
    [property: JsonPropertyName("workDayEnd")] string WorkDayEnd,
    [property: JsonPropertyName("lateThreshold")] string LateThreshold,
    // Чек
    [property: JsonPropertyName("receiptHeader")] string? ReceiptHeader,
    [property: JsonPropertyName("receiptFooter")] string? ReceiptFooter,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt,
    [property: JsonPropertyName("receiptWidthMm")] int ReceiptWidthMm,
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
    // Telegram bog'lash bu yerda emas — har bir xodim Account'da botning bir
    // martalik kodi bilan bog'lanadi (User.TelegramChatId, TelegramLinkCode).
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
    [property: JsonPropertyName("workDayStart")] string WorkDayStart,
    [property: JsonPropertyName("workDayEnd")] string WorkDayEnd,
    [property: JsonPropertyName("lateThreshold")] string LateThreshold,
    [property: JsonPropertyName("receiptHeader")] string? ReceiptHeader,
    [property: JsonPropertyName("receiptFooter")] string? ReceiptFooter,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt,
    [property: JsonPropertyName("receiptWidthMm")] int ReceiptWidthMm,
    [property: JsonPropertyName("defaultLanguage")] string DefaultLanguage,
    [property: JsonPropertyName("firstDayOfWeek")] int FirstDayOfWeek,
    [property: JsonPropertyName("minStockAlertEnabled")] bool MinStockAlertEnabled,
    [property: JsonPropertyName("blockSaleBelowCost")] bool BlockSaleBelowCost,
    [property: JsonPropertyName("defaultMarkupPct")] decimal DefaultMarkupPct,
    [property: JsonPropertyName("notifyDaySummary")] bool NotifyDaySummary,
    [property: JsonPropertyName("notifyOverdueDebts")] bool NotifyOverdueDebts,
    [property: JsonPropertyName("notifyWithdrawalRequests")] bool NotifyWithdrawalRequests,
    [property: JsonPropertyName("inactivityLogoutMinutes")] int InactivityLogoutMinutes,
    [property: JsonPropertyName("auditEnabled")] bool AuditEnabled
);
