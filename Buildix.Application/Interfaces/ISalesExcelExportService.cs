using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Builds the sales-list Excel workbook (bilingual). The cost and profit columns
/// are each masked "—" when their permission flag is false — the controller passes
/// <c>canViewCost</c> (data.costPrice) and <c>canViewProfit</c> (data.profit). The
/// service never touches HttpContext; the controller only wraps it in File(...).
/// </summary>
public interface ISalesExcelExportService
{
    /// <param name="from">Optional UTC range start — null exports every sale
    /// (the web «Экспорт» button). The Telegram bot passes a single business day.</param>
    /// <param name="to">Optional UTC range end (exclusive).</param>
    /// <param name="sellerId">Non-null limits the workbook to that seller's own
    /// receipts — for a cashier without <c>data.allSalesView</c>.</param>
    Task<ExcelExportResult> ExportSalesAsync(string lang, bool canViewCost, bool canViewProfit,
        DateTime? from = null, DateTime? to = null, Guid? sellerId = null,
        CancellationToken cancellationToken = default);
}
