using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces.Reports;
using Buildix.API.Authorization;
using Buildix.Domain.Constants;
using System.Security.Claims;

namespace Buildix.API.Controllers;

/// <summary>
/// Thin reporting controller. All business logic lives in the focused report
/// services (Reports/*); each action derives the caller's role/id from the JWT
/// claims (services never touch HttpContext) and delegates. The former
/// monolithic IReportService facade has been removed.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController(
    ISalesReportService salesReportService,
    ISalesListService salesListService,
    IFinancialReportService financialReportService,
    IDashboardService dashboardService,
    IReportPdfExportService reportPdfExportService,
    IReportExcelExportService reportExcelExportService,
    ILogger<ReportsController> logger) : ApiControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ISalesReportService _salesReportService = salesReportService;
    private readonly ISalesListService _salesListService = salesListService;
    private readonly IFinancialReportService _financialReportService = financialReportService;
    private readonly IDashboardService _dashboardService = dashboardService;
    private readonly IReportPdfExportService _reportPdfExportService = reportPdfExportService;
    private readonly IReportExcelExportService _reportExcelExportService = reportExcelExportService;
    private readonly ILogger<ReportsController> _logger = logger;

    // Cost-price visibility is gated by data.costPrice (not a hardcoded role).
    private bool CanViewCost() => HasPermission(PermissionKeys.DataCostPrice);

    // Profit visibility is gated by data.profit: Owner/SuperAdmin always; Admin
    // only while granted (default Admin lacks it); Seller never.
    private bool CanViewProfit() => HasPermission(PermissionKeys.DataProfit);

    // ── Reports (ISalesReportService) ────────────────────────────────────────

    /// <summary>Get daily sales report.</summary>
    [HttpGet("daily")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<DailyReportDto>> GetDailyReport([FromQuery] DateTime date)
    {
        var report = await _salesReportService.GetDailyReportAsync(date, CanViewProfit());
        return Ok(report);
    }

    /// <summary>Get daily sale items — detailed list of products sold on a date.</summary>
    [HttpGet("daily-items")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<DailySaleItemsResponseDto>> GetDailySaleItems([FromQuery] DateTime date)
    {
        var saleItems = await _salesReportService.GetDailySaleItemsAsync(date, CanViewProfit());
        return Ok(saleItems);
    }

    /// <summary>Get sales report for a period.</summary>
    [HttpGet("period")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<PeriodReportDto>> GetPeriodReport(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        if (start > end)
            return BadRequest("Start date cannot be after end date");

        var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
        var utcEnd = DateTime.SpecifyKind(end.Date, DateTimeKind.Utc);

        var request = new PeriodReportRequest(utcStart, utcEnd);
        var report = await _salesReportService.GetPeriodReportAsync(request, CanViewProfit());
        return Ok(report);
    }

    /// <summary>Get comprehensive report including seller stats and inventory.</summary>
    [HttpGet("comprehensive")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<ComprehensiveReportDto>> GetComprehensiveReport([FromQuery] DateTime date)
    {
        var report = await _salesReportService.GetComprehensiveReportAsync(date, CanViewProfit());
        return Ok(report);
    }

    // ── Financial (IFinancialReportService) ──────────────────────────────────

    /// <summary>Get profit summary — Owner only.</summary>
    [HttpGet("profit-summary")]
    [RequirePermission(PermissionKeys.DataProfit)]
    public async Task<ActionResult<ProfitSummaryDto>> GetProfitSummary()
    {
        var summary = await _financialReportService.GetProfitSummaryAsync();
        return Ok(summary);
    }

    /// <summary>Get cash balance — Owner only.</summary>
    [HttpGet("cash-balance")]
    [RequirePermission(PermissionKeys.DataCashBalance)]
    public async Task<ActionResult<CashBalanceDto>> GetCashBalance()
    {
        var balance = await _financialReportService.GetCashBalanceAsync();
        return Ok(balance);
    }

    // ── Dashboard (IDashboardService) ────────────────────────────────────────

    /// <summary>
    /// Last N days of revenue/profit/check counts as a time series (dashboard
    /// ChartCard). Defaults to 7 days; the 30-day cap lives inside the service.
    /// Profit is zero unless the caller is Owner.
    /// </summary>
    [HttpGet("weekly-series")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<WeeklySeriesDto>> GetWeeklySeries(
        [FromQuery] int days = 7,
        [FromQuery] bool compare = false)
    {
        var series = await _dashboardService.GetWeeklySeriesAsync(days, compare, CanViewProfit());
        return Ok(series);
    }

    /// <summary>Top-N products in the period, ranked by quantity / revenue / profit.</summary>
    [HttpGet("top-products")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<TopProductsDto>> GetTopProducts(
        [FromQuery] string period = "month",
        [FromQuery] string sortBy = "quantity",
        [FromQuery] int limit = 10)
    {
        var products = await _dashboardService.GetTopProductsAsync(period, sortBy, limit, CanViewProfit());
        return Ok(products);
    }

    /// <summary>Per-staff sales metrics for the period (includes zero-sales staff).</summary>
    [HttpGet("staff-performance")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<StaffPerformanceDto>> GetStaffPerformance(
        [FromQuery] string period = "week")
    {
        var staff = await _dashboardService.GetStaffPerformanceAsync(period);
        return Ok(staff);
    }

    /// <summary>The current user's own sales metrics for the period (default: today).</summary>
    [HttpGet("my-performance")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<MyPerformanceDto>> GetMyPerformance(
        [FromQuery] string period = "today")
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var result = await _dashboardService.GetMyPerformanceAsync(userId, period);
        return Ok(result);
    }

    /// <summary>
    /// Pre-aggregated counters for the Owner dashboard (customer count,
    /// low-stock count, pending / overdue debts).
    /// </summary>
    [HttpGet("dashboard-summary")]
    [RequirePermission(PermissionKeys.ReportsAccess)]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary(
        CancellationToken cancellationToken)
    {
        var summary = await _dashboardService.GetOwnerDashboardSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    // ── Sales lists (ISalesListService) ──────────────────────────────────────

    /// <summary>
    /// Daily sales list with role-based filtering (Owner sees all + profit;
    /// Admin sees all without profit; Seller sees only their own).
    /// </summary>
    [HttpGet("daily-sales-list")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<DailySalesListDto>> GetDailySalesList([FromQuery] DateTime date)
    {
        var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

        var salesList = await _salesListService.GetDailySalesListAsync(date, userRole, CanViewProfit(), userId);
        return Ok(salesList);
    }

    /// <summary>Monthly sales grouped by product category.</summary>
    [HttpGet("monthly-category-sales")]
    [RequirePermission(PermissionKeys.SalesAccess)]
    public async Task<ActionResult<MonthlyCategorySalesResponseDto>> GetMonthlyCategorySales(
        [FromQuery] DateTime date, CancellationToken cancellationToken = default)
    {
        var report = await _salesListService.GetMonthlyCategorySalesAsync(date, CanViewProfit(), cancellationToken);
        return Ok(report);
    }

    // ── PDF exports (IReportPdfExportService) ────────────────────────────────

    [HttpGet("daily/export-pdf")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportDailyReportToPdf([FromQuery] DateTime date, [FromQuery] string lang = "uz")
    {
        var utcDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var pdfBytes = await _reportPdfExportService.ExportDailyReportToPdfAsync(utcDate, CanViewProfit(), lang);

        return File(pdfBytes, "application/pdf", $"daily_report_{date:yyyyMMdd}.pdf");
    }

    [HttpGet("period/export-pdf")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportPeriodReportToPdf(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end,
        [FromQuery] string lang = "uz")
    {
        if (start > end)
            return BadRequest("Start date cannot be after end date");

        var utcStart = DateTime.SpecifyKind(start.Date, DateTimeKind.Utc);
        var utcEnd = DateTime.SpecifyKind(end.Date, DateTimeKind.Utc);

        var request = new PeriodReportRequest(utcStart, utcEnd);
        var pdfBytes = await _reportPdfExportService.ExportPeriodReportToPdfAsync(request, CanViewProfit(), lang);

        return File(pdfBytes, "application/pdf", $"period_report_{start:yyyyMMdd}_{end:yyyyMMdd}.pdf");
    }

    [HttpGet("comprehensive/export-pdf")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportComprehensiveReportToPdf([FromQuery] DateTime date, [FromQuery] string lang = "uz")
    {
        var utcDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var pdfBytes = await _reportPdfExportService.ExportComprehensiveReportToPdfAsync(utcDate, CanViewProfit(), lang);

        return File(pdfBytes, "application/pdf", $"comprehensive_report_{date:yyyyMMdd}.pdf");
    }

    // ── Excel exports (IReportExcelExportService) ────────────────────────────
    // Workbook building lives in the service; the controller keeps the existing
    // try/catch → 500 shape (a service cannot return an ActionResult).

    /// <summary>Export the comprehensive daily report to a multi-sheet Excel workbook.</summary>
    [HttpGet("comprehensive-report/export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportComprehensiveReportToExcel(
        [FromQuery] DateTime? date = null,
        [FromQuery] string lang = "uz",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

            var result = await _reportExcelExportService.ExportComprehensiveReportAsync(date, lang, userRole, userId, CanViewProfit(), cancellationToken);
            return File(result.Content, XlsxContentType, result.FileName);
        }
        catch (Exception ex) when (NotHandledGlobally(ex))
        {
            _logger.LogError(ex, "Error exporting comprehensive report to Excel");
            return StatusCode(500, new { message = "Xatolik yuz berdi", error = ex.Message });
        }
    }

    /// <summary>Export the current warehouse (Ombor) inventory to Excel.</summary>
    [HttpGet("inventory-report/export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportInventoryReportToExcel(
        [FromQuery] DateTime? date = null,
        [FromQuery] string lang = "uz",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            var result = await _reportExcelExportService.ExportInventoryReportAsync(date, lang, userRole, CanViewCost(), CanViewProfit(), cancellationToken);
            return File(result.Content, XlsxContentType, result.FileName);
        }
        catch (Exception ex) when (NotHandledGlobally(ex))
        {
            _logger.LogError(ex, "Error exporting warehouse report to Excel");
            return StatusCode(500, new { message = "Xatolik yuz berdi", error = ex.Message });
        }
    }

    /// <summary>Export daily report to Excel — kunlik hisobot, sotuvlar ro'yxati va mahsulotlar bo'yicha.</summary>
    [HttpGet("daily/export")]
    [EnableRateLimiting("export")]
    [RequirePermission(PermissionKeys.ReportsExport)]
    public async Task<IActionResult> ExportDailyReportToExcel([FromQuery] DateTime date)
    {
        try
        {
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = Guid.TryParse(userIdString, out var parsedId) ? parsedId : null;

            var result = await _reportExcelExportService.ExportDailyReportAsync(date, userRole, userId, CanViewProfit());
            return File(result.Content, XlsxContentType, result.FileName);
        }
        catch (Exception ex) when (NotHandledGlobally(ex))
        {
            _logger.LogError(ex, "Error exporting daily report to Excel");
            return StatusCode(500, new { message = "Xatolik yuz berdi", error = ex.Message });
        }
    }
}
