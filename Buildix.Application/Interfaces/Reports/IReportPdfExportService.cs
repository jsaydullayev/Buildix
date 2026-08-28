using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces.Reports;

/// <summary>
/// PDF export orchestration — daily / period / comprehensive report PDFs, the
/// sales-list PDF, and sale invoices. <paramref name="lang"/> ("uz" | "ru")
/// localises the document; <c>compact</c> renders the print-friendly invoice.
/// </summary>
public interface IReportPdfExportService
{
    Task<byte[]> ExportSalesListToPdfAsync(DateTime? startDate, DateTime? endDate, bool canViewCost = false, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default);
    Task<byte[]> ExportDailyReportToPdfAsync(DateTime date, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default);
    Task<byte[]> ExportPeriodReportToPdfAsync(PeriodReportRequest request, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default);
    Task<byte[]> ExportComprehensiveReportToPdfAsync(DateTime date, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default);
    Task<byte[]> GenerateInvoicePdfAsync(Guid saleId, string lang = "uz", bool compact = false, CancellationToken cancellationToken = default);

    /// <summary>Kassa cheki — termal rulon (58/80 mm) uchun. A4 fakturadan
    /// farqli o'laroq XPrinter kabi printerlarga to'g'ridan-to'g'ri tushadi.</summary>
    Task<byte[]> GenerateThermalReceiptPdfAsync(Guid saleId, string lang = "uz", int widthMm = 80, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chekning rasmi (PNG) — do'kon dasturi uni chop etish oynasisiz,
    /// to'g'ridan-to'g'ri sozlangan printerga yuboradi.
    /// </summary>
    Task<byte[]> GenerateThermalReceiptImageAsync(Guid saleId, string lang = "uz", int widthMm = 80, CancellationToken cancellationToken = default);
}
