using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Extensions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
// Brings ReportPdfRenderer's static renderers + nested carrier types
// (InvoiceData, InvoiceItemData, SalesReportItem, PdfTheme) into scope unqualified.
using static Buildix.Application.Services.Reports.ReportPdfRenderer;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Orchestrates PDF exports — fetches the data (via focused report services /
/// repositories) and hands it to the pure <see cref="ReportPdfRenderer"/>.
/// Extracted from the former 2700-line ReportService.
/// </summary>
public sealed class ReportPdfExportService(
    IUnitOfWork unitOfWork,
    ICurrentMarketService currentMarketService,
    ITashkentClock clock,
    ILogger<ReportPdfExportService> logger,
    ISalesReportService salesReportService,
    IMarketSettingsService marketSettingsService)
    : ReportServiceBase(clock), IReportPdfExportService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentMarketService _currentMarketService = currentMarketService;
    private readonly ILogger<ReportPdfExportService> _logger = logger;
    private readonly ISalesReportService _salesReportService = salesReportService;
    private readonly IMarketSettingsService _marketSettingsService = marketSettingsService;

    public async Task<byte[]> ExportSalesListToPdfAsync(DateTime? startDate, DateTime? endDate, bool canViewCost = false, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default)
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var marketId = _currentMarketService.GetCurrentMarketId();

        // Get all sales if no date range specified
        var start = startDate.HasValue ? ToUtcDate(startDate.Value) : DateTime.MinValue;
        var end = endDate.HasValue ? ToUtcDate(endDate.Value.AddDays(1)) : DateTime.UtcNow.AddDays(1);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= start && s.CreatedAt < end &&
                 s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                 s.MarketId == marketId, q => q.Include(e => e.SaleItems).Include(e => e.Payments).Include(e => e.Seller).Include(e => e.Customer), cancellationToken);

        // Get all products for the sale items
        var allProductIds = sales.SelectMany(s => s.SaleItems).Select(si => si.ProductId).Distinct().ToList();
        var products = new Dictionary<Guid, string>();
        if (allProductIds.Any())
        {
            var productList = await _unitOfWork.Products.FindAsync(
                p => allProductIds.Contains(p.Id) && p.MarketId == marketId,
                cancellationToken);
            foreach (var p in productList)
            {
                products[p.Id] = p.Name;
            }
        }

        // Owner role check - null or non-owner means no profit visibility.
        // Cost price is visible to Owner AND Admin (data.costPrice), but never
        // to a Seller — so the Xarid column is masked for Sellers.
        bool includeProfit = canViewProfit;
        bool includeCost = canViewCost;
        decimal totalSales = 0;
        decimal totalProfit = 0;

        var reportItems = new List<SalesReportItem>();
        int itemNumber = 1;

        // Sort sales by date descending
        var sortedSales = sales.OrderByDescending(s => s.CreatedAt).ToList();

        foreach (var sale in sortedSales)
        {
            // Calculate paid ratio for debt sales (only count profit for paid portion)
            decimal paidRatio = sale.TotalAmount > 0
                ? sale.PaidAmount / sale.TotalAmount
                : 1;

            foreach (var item in sale.SaleItems)
            {
                // ✅ ISEXTERNAL SHARTI - Product name olish
                string productName;
                decimal costPrice;

                if (!item.IsExternal)
                {
                    productName = item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var name) ? name : "Unknown";
                    costPrice = item.CostPrice;
                }
                else
                {
                    productName = item.ExternalProductName ?? "Tashqi mahsulot";
                    costPrice = item.ExternalCostPrice;
                }

                // Full item profit
                decimal fullItemProfit = (item.SalePrice - costPrice) * item.Quantity;
                // Adjusted profit based on paid ratio (consistent with Excel export)
                decimal adjustedProfit = fullItemProfit * paidRatio;

                totalSales += item.TotalPrice;
                if (includeProfit)
                {
                    totalProfit += adjustedProfit;
                }

                reportItems.Add(new SalesReportItem(
                    itemNumber++,
                    sale.CreatedAt,
                    sale.Customer?.FullName ?? L("Mijoz yo'q", "Без клиента"),
                    sale.Seller?.FullName ?? L("Noma'lum", "Неизвестно"),
                    productName,
                    item.Quantity,
                    costPrice,  // ✅ Effective cost price (ExternalCostPrice for external products)
                    item.SalePrice,
                    item.TotalPrice,
                    includeProfit ? adjustedProfit : null,
                    sale.Status.ToString()
                ));
            }

            // Chegirma (skidka): item qatorlari GROSS narxda qoladi — tovar narxi
            // o'zgarmaydi — lekin sotuvning jami tushumi (va foydasi) chegirma
            // hisobiga kamayadi, shunda PDF'dagi yakuniy summa sale.TotalAmount
            // (net) bilan mos keladi.
            totalSales -= sale.DiscountAmount;
            if (includeProfit)
            {
                totalProfit -= sale.DiscountAmount * paidRatio;
            }
        }


        try
        {
            _logger.LogInformation($"[ExportSalesListToPdfAsync] Starting PDF generation for {reportItems.Count} items");
            return ReportPdfRenderer.RenderSalesListPdf(reportItems, startDate, endDate, includeProfit, includeCost, totalSales, totalProfit, sortedSales.Count, _clock.NowLocal, lang);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Sales list PDF generation failed: {ex.Message}", ex);
        }
    }

    public async Task<byte[]> ExportDailyReportToPdfAsync(DateTime date, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default)
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var report = await _salesReportService.GetDailyReportAsync(date, canViewProfit, cancellationToken);
        var kpis = new List<(string Label, string Value, string Accent)>
        {
            (L("Jami savdo", "Общая выручка"),   $"{report.TotalSales:N0}{L(" so'm", " сум")}",     PdfTheme.Ink),
            (L("To'langan", "Оплачено"),         $"{report.TotalPaidSales:N0}{L(" so'm", " сум")}", PdfTheme.Success),
            (L("Qarz", "Долг"),                  $"{report.TotalDebtSales:N0}{L(" so'm", " сум")}", PdfTheme.Danger),
            (L("Cheklar soni", "Кол-во чеков"),  $"{report.TotalTransactions:N0}",   PdfTheme.Ink),
            (L("Zakup", "Закуп"),                $"{report.TotalZakup:N0}{L(" so'm", " сум")}",     PdfTheme.Muted),
        };
        if (report.Profit.HasValue)
            kpis.Add((L("Sof foyda", "Чистая прибыль"), $"{report.Profit.Value:N0}{L(" so'm", " сум")}",
                report.Profit.Value >= 0 ? PdfTheme.Success : PdfTheme.Danger));

        return ReportPdfRenderer.RenderSummaryReportPdf(L("KUNLIK HISOBOT", "ДНЕВНОЙ ОТЧЁТ"), date.ToString("dd.MM.yyyy"),
            kpis, report.PaymentBreakdown, _clock.NowLocal, lang);
    }

    public async Task<byte[]> ExportPeriodReportToPdfAsync(PeriodReportRequest request, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default)
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var report = await _salesReportService.GetPeriodReportAsync(request, canViewProfit, cancellationToken);
        var kpis = new List<(string Label, string Value, string Accent)>
        {
            (L("Jami savdo", "Общая выручка"),   $"{report.TotalSales:N0}{L(" so'm", " сум")}",     PdfTheme.Ink),
            (L("To'langan", "Оплачено"),         $"{report.TotalPaidSales:N0}{L(" so'm", " сум")}", PdfTheme.Success),
            (L("Qarz", "Долг"),                  $"{report.TotalDebtSales:N0}{L(" so'm", " сум")}", PdfTheme.Danger),
            (L("Cheklar soni", "Кол-во чеков"),  $"{report.TotalTransactions:N0}",   PdfTheme.Ink),
            (L("O'rtacha chek", "Средний чек"),  $"{report.AverageSale:N0}{L(" so'm", " сум")}",    PdfTheme.Ink),
            (L("Zakup", "Закуп"),                $"{report.TotalZakup:N0}{L(" so'm", " сум")}",     PdfTheme.Muted),
        };
        if (report.Profit.HasValue)
            kpis.Add((L("Sof foyda", "Чистая прибыль"), $"{report.Profit.Value:N0}{L(" so'm", " сум")}",
                report.Profit.Value >= 0 ? PdfTheme.Success : PdfTheme.Danger));

        var period = $"{request.StartDate:dd.MM.yyyy} — {request.EndDate:dd.MM.yyyy}";
        return ReportPdfRenderer.RenderSummaryReportPdf(L("DAVRIY HISOBOT", "ОТЧЁТ ЗА ПЕРИОД"), period, kpis, report.PaymentBreakdown, _clock.NowLocal, lang);
    }

    public async Task<byte[]> ExportComprehensiveReportToPdfAsync(DateTime date, bool canViewProfit = false, string lang = "uz", CancellationToken cancellationToken = default)
    {
        var report = await _salesReportService.GetComprehensiveReportAsync(date, canViewProfit, cancellationToken);
        return ReportPdfRenderer.RenderComprehensiveReportPdf(report, date.ToString("dd.MM.yyyy"), _clock.NowLocal, lang);
    }

    /// <summary>
    /// Chek — termal rulon uchun. Ma'lumot yig'ish A4 faktura bilan bir xil
    /// (<see cref="BuildInvoiceDataAsync"/>), farqi faqat chizishda.
    /// </summary>
    public async Task<byte[]> GenerateThermalReceiptPdfAsync(Guid saleId, string lang = "uz", int widthMm = 80, CancellationToken cancellationToken = default)
    {
        var data = await BuildInvoiceDataAsync(saleId, lang, cancellationToken);
        try
        {
            return ReportPdfRenderer.RenderThermalReceiptPdf(data, lang, widthMm);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Receipt generation failed for sale {saleId}: {ex.Message}", ex);
        }
    }

    public async Task<byte[]> GenerateThermalReceiptImageAsync(Guid saleId, string lang = "uz", int widthMm = 80, CancellationToken cancellationToken = default)
    {
        var data = await BuildInvoiceDataAsync(saleId, lang, cancellationToken);
        try
        {
            return ReportPdfRenderer.RenderThermalReceiptPng(data, lang, widthMm);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Receipt image failed for sale {saleId}: {ex.Message}", ex);
        }
    }

    public async Task<byte[]> GenerateReceiptEscPosAsync(Guid saleId, string lang = "uz", int widthMm = 80, CancellationToken cancellationToken = default)
    {
        var data = await BuildInvoiceDataAsync(saleId, lang, cancellationToken);
        return Printing.EscPosReceipt.Build(data, lang, widthMm);
    }

    public async Task<byte[]> GenerateInvoicePdfAsync(Guid saleId, string lang = "uz", bool compact = false, CancellationToken cancellationToken = default)
    {
        var invoiceData = await BuildInvoiceDataAsync(saleId, lang, cancellationToken);
        try
        {
            return compact
                ? ReportPdfRenderer.RenderInvoiceCompactPdf(invoiceData, lang)
                : ReportPdfRenderer.RenderInvoicePdf(invoiceData, lang);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF generation failed for sale {saleId}: {ex.Message}", ex);
        }
    }

    /// <summary>Sotuvni o'qib, chek/faktura uchun umumiy ma'lumot to'plamini yig'adi.</summary>
    private async Task<ReportPdfRenderer.InvoiceData> BuildInvoiceDataAsync(Guid saleId, string lang, CancellationToken cancellationToken)
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var marketId = _currentMarketService.GetCurrentMarketId();

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.Id == saleId && s.MarketId == marketId, q => q.Include(e => e.SaleItems).Include(e => e.Payments).Include(e => e.Seller).Include(e => e.Customer).Include(e => e.Market), cancellationToken);

        var sale = sales.FirstOrDefault();
        if (sale == null)
            throw new KeyNotFoundException($"Sale with ID {saleId} not found.");

        if (sale.Market == null)
            throw new InvalidOperationException($"Market data is missing for sale {saleId}.");

        var market = sale.Market;
        var seller = sale.Seller;

        // If seller was soft-deleted, fetch their name directly from database (ignoring soft-delete filter)
        string sellerName = L("Noma'lum sotuvchi", "Продавец не указан");
        if (seller != null)
        {
            sellerName = seller.FullName;
        }
        else
        {
            // Try to get seller name from database even if deleted
            var deletedSeller = await _unitOfWork.Users.GetByIdIncludingDeletedAsync(sale.SellerId, cancellationToken);
            if (deletedSeller != null)
            {
                sellerName = deletedSeller.FullName;
            }
        }

        var customer = sale.Customer;

        // If customer was soft-deleted, fetch their name from database (ignoring soft-delete filter)
        string noCustomer = L("Mijoz ko'rsatilmagan", "Без клиента");
        string customerName = noCustomer;
        if (customer != null)
        {
            customerName = customer.FullName ?? noCustomer;
        }
        else if (sale.CustomerId.HasValue)
        {
            // Try to get customer name from database even if deleted
            var deletedCustomer = await _unitOfWork.Customers.GetByIdIncludingDeletedAsync(sale.CustomerId.Value, cancellationToken);
            if (deletedCustomer != null)
            {
                customerName = deletedCustomer.FullName ?? noCustomer;
            }
        }

        // ✅ Get all ordinary products for the sale items (faqat oddiy mahsulotlar uchun)
        var ordinaryProductIds = sale.SaleItems
            .Where(si => !si.IsExternal && si.ProductId.HasValue)
            .Select(si => si.ProductId!.Value)
            .Distinct()
            .ToList();

        var products = new Dictionary<Guid, Product>();
        if (ordinaryProductIds.Any())
        {
            var productList = await _unitOfWork.Products.FindAsync(
                p => ordinaryProductIds.Contains(p.Id) && p.MarketId == marketId,
                cancellationToken);
            foreach (var p in productList)
            {
                products[p.Id] = p;
            }
        }
        var productDict = products;

        // Determine payment type
        var primaryPayment = sale.Payments.FirstOrDefault(p => p.Amount > 0);
        var paymentTypeEnum = primaryPayment?.PaymentType ?? PaymentType.Cash;
        string paymentTypeUz = isRu
            ? paymentTypeEnum switch
            {
                PaymentType.Cash => "Наличные",
                PaymentType.Transfer => "Перевод / Счёт",
                _ => paymentTypeEnum.ToString(), // Terminal / Click — already fine
            }
            : paymentTypeEnum.ToUzbek();

        // Create invoice data
        var invoiceItems = new List<InvoiceItemData>();
        foreach (var item in sale.SaleItems)
        {
            // ✅ ISEXTERNAL SHARTI - Product name olish
            string productName;
            if (!item.IsExternal)
            {
                // Oddiy mahsulot - ProductId nullable uchun null check
                if (!item.ProductId.HasValue)
                    productName = "Unknown";
                else
                {
                    var product = productDict.GetValueOrDefault(item.ProductId.Value);
                    productName = product?.Name ?? L("Noma'lum mahsulot", "Неизвестный товар");
                }
            }
            else
            {
                // Tashqi mahsulot
                productName = item.ExternalProductName ?? L("Noma'lum mahsulot", "Неизвестный товар");
            }
            invoiceItems.Add(new InvoiceItemData(
                productName,
                item.Quantity,
                item.SalePrice,
                item.SalePrice * item.Quantity,
                item.Comment,
                item.IsExternal
            ));
        }

        // Do'kon rekvizitlari — manzil, telefon va egasi yozgan chek matnlari.
        // Ular MARKET da emas, MarketSettings da yotadi: ega ularni Sozlamalar
        // ekranida to'ldiradi. Ilgari chekka umuman yetib bormasdi va ega
        // «Chek tepasidagi matn» ni yozib, uni qog'ozda hech qachon ko'rmasdi.
        var settings = await _marketSettingsService.GetOrCreateAsync(marketId, cancellationToken);

        var invoiceData = new ReportPdfRenderer.InvoiceData(
            market.Name,
            market.Description ?? "",
            sellerName,
            customerName,
            sale.Id,
            sale.CreatedAt,
            paymentTypeUz,
            invoiceItems,
            sale.TotalAmount,
            sale.PaidAmount,
            sale.TotalAmount - sale.PaidAmount,
            sale.Status.ToString(), // raw enum — RenderInvoicePdf localises it
            // Oraliq summa = chegirmagacha bo'lgan qatorlar yig'indisi;
            // TotalAmount esa allaqachon net (gross − chegirma).
            invoiceItems.Sum(i => i.Total),
            sale.DiscountAmount,
            MarketAddress: settings.Address,
            MarketPhone: settings.Phone,
            ReceiptHeader: settings.ReceiptHeader,
            ReceiptFooter: settings.ReceiptFooter
        );

        return invoiceData;
    }
}
