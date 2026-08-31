using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Extensions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Daily / period / comprehensive sales reports + per-day sale-item breakdown,
/// extracted verbatim from the former 2700-line ReportService. Shares the
/// <c>CalculateReport</c> aggregation core across the three report shapes.
/// </summary>
public sealed class SalesReportService(
    IUnitOfWork unitOfWork,
    ICurrentMarketService currentMarketService,
    IAppDbContext context,
    ITashkentClock clock,
    ILogger<SalesReportService> logger)
    : ReportServiceBase(clock), ISalesReportService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentMarketService _currentMarketService = currentMarketService;
    private readonly IAppDbContext _context = context;
    private readonly ILogger<SalesReportService> _logger = logger;

    public async Task<DailyReportDto> GetDailyReportAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var (start, end) = GetUtcDateRange(date);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= start && s.CreatedAt < end && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.SaleItems).Include(e => e.Payments), cancellationToken);

        var zakups = await _unitOfWork.Zakups.FindAsync(
            z => z.CreatedAt >= start && z.CreatedAt < end && z.MarketId == marketId,
            cancellationToken);

        return CalculateReport(sales, zakups, start, end, canViewProfit);
    }

    public async Task<DailySaleItemsResponseDto> GetDailySaleItemsAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var (start, end) = GetUtcDateRange(date);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= start && s.CreatedAt < end && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.SaleItems), cancellationToken);

        // ✅ Batch fetch all ordinary products to avoid N+1 query (faqat oddiy mahsulotlar uchun)
        var ordinaryProductIds = sales
            .SelectMany(s => s.SaleItems)
            .Where(si => !si.IsExternal && si.ProductId.HasValue)
            // The Where above already guarantees HasValue; the null-forgiving
            // `!` just tells the compiler what the filter cannot express.
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

        bool includeProfit = canViewProfit;

        var allItems = new List<DailySaleItemDto>();

        _logger.LogInformation("[GetDailySaleItems] Processing {Count} sales for {Date}", sales.Count(), start.ToString("yyyy-MM-dd"));

        foreach (var sale in sales)
        {
            foreach (var item in sale.SaleItems)
            {
                // ✅ ISEXTERNAL SHARTI - Product name va CostPrice
                string productName;
                decimal costPrice;
                string unit = "";

                if (!item.IsExternal)
                {
                    // Oddiy mahsulot. A non-external item should always carry
                    // a ProductId, but the column is nullable — guard so a
                    // bad row is skipped instead of throwing.
                    if (!item.ProductId.HasValue ||
                        !products.TryGetValue(item.ProductId.Value, out var product))
                        continue;

                    productName = product.Name;
                    costPrice = item.CostPrice;
                    unit = product.GetUnitName();
                }
                else
                {
                    // Tashqi mahsulot
                    productName = item.ExternalProductName ?? "Tashqi mahsulot";
                    costPrice = item.ExternalCostPrice;
                    // Unit bo'sh qoldiriladi
                }
                var quantity = item.Quantity;

                if (quantity % 1 != 0)
                {
                    _logger.LogInformation("Double quantity: {ProductName} - {Quantity} ta (Sale: {SaleId})", productName, quantity, sale.Id);
                }

                var salePrice = item.SalePrice;
                var totalCost = costPrice * quantity;
                var totalRevenue = salePrice * quantity;
                decimal? profit = includeProfit ? totalRevenue - totalCost : null;

                allItems.Add(new DailySaleItemDto(
                    productName,
                    quantity,
                    costPrice,
                    salePrice,
                    totalCost,
                    totalRevenue,
                    profit
                ));
            }
        }

        var sortedItems = allItems.OrderByDescending(i => i.Quantity).ToList();

        return new DailySaleItemsResponseDto(
            start,
            sortedItems
        );
    }

    public async Task<PeriodReportDto> GetPeriodReportAsync(PeriodReportRequest request, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Use < instead of <= to include the entire end day (up to 23:59:59.999)
        var endDateTime = request.EndDate.AddDays(1);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= request.StartDate && s.CreatedAt < endDateTime && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.SaleItems).Include(e => e.Payments), cancellationToken);

        var zakups = await _unitOfWork.Zakups.FindAsync(
            z => z.CreatedAt >= request.StartDate && z.CreatedAt < endDateTime && z.MarketId == marketId,
            cancellationToken);

        var report = CalculateReport(sales, zakups, request.StartDate, request.EndDate, canViewProfit);

        decimal averageSale = report.TotalTransactions > 0
            ? report.TotalSales / report.TotalTransactions
            : 0;

        return new PeriodReportDto(
            request.StartDate,
            request.EndDate,
            report.TotalSales,
            report.TotalPaidSales,
            report.TotalDebtSales,
            report.TotalZakup,
            report.Profit,
            report.NetIncome,
            report.TotalTransactions,
            averageSale,
            report.PaymentBreakdown
        );
    }

    public async Task<ComprehensiveReportDto> GetComprehensiveReportAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var (start, end) = GetUtcDateRange(date);

        // Get daily sales with SaleItems and Payments
        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= start && s.CreatedAt < end && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.SaleItems).Include(e => e.Payments), cancellationToken);

        // Get zakups
        var zakups = await _unitOfWork.Zakups.FindAsync(
            z => z.CreatedAt >= start && z.CreatedAt < end && z.MarketId == marketId,
            cancellationToken);

        // P6 — Projection o'rniga to'liq entity yuklash:
        // 10K tovar × ~20 ustun + Category navigation = ~100MB memory.
        // Select projection faqat kerakli 7 ustunni oladi → ~15MB.
        var productProjections = await _context.Products
            .AsNoTracking()
            .Where(p => p.MarketId == marketId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Quantity,
                p.MinThreshold,
                p.CostPrice,
                p.SalePrice,
                p.MinSalePrice,
                p.Unit,
                CategoryName = p.Category != null ? p.Category.Name : (string?)null,
            })
            .ToListAsync(cancellationToken);

        // Calculate daily report
        var dailyReport = CalculateReport(sales, zakups, start, end);

        // Calculate seller reports - ONLY FOR OWNER
        var sellerReports = new List<SellerReportDto>();

        // Only callers with data.profit can see per-seller profit reports.
        if (canViewProfit)
        {
            // Get all users for seller reports (filtered by market)
            var users = await _unitOfWork.Users.FindAsync(
                u => u.MarketId == marketId,
                cancellationToken);

            foreach (var user in users.Where(u => u.Role == Role.Seller || u.Role == Role.Admin || u.Role == Role.Owner))
            {
                var userSales = sales.Where(s => s.SellerId == user.Id).ToList();
                if (userSales.Any())
                {
                    decimal totalSales = userSales.Sum(s => s.TotalAmount);
                    decimal totalProfit = 0;

                    foreach (var sale in userSales)
                    {
                        foreach (var item in sale.SaleItems)
                        {
                            // ✅ ISEXTERNAL SHARTI - Effective cost price
                            decimal costPrice = item.IsExternal ? item.ExternalCostPrice : item.CostPrice;
                            var itemCost = costPrice * item.Quantity;
                            var itemRevenue = item.SalePrice * item.Quantity;
                            totalProfit += itemRevenue - itemCost;
                        }
                        // Sale-level discount reduces this seller's profit once
                        // per sale (gross item-profit above is overstated by it).
                        totalProfit -= sale.DiscountAmount;
                    }

                    sellerReports.Add(new SellerReportDto(
                        user.Id,
                        user.FullName,
                        totalSales,
                        totalProfit,
                        userSales.Count
                    ));
                }
            }
        }

        // Calculate inventory report
        var inventoryReport = new List<InventoryReportDto>();
        decimal totalInventoryCost = 0;
        decimal totalInventorySaleValue = 0;

        // Determine if profit should be included (Owner only)
        bool includeProfit = canViewProfit;

        // Calculate inventory statistics
        int productCount = productProjections.Count;
        int lowStockCount = productProjections.Count(p => p.Quantity <= p.MinThreshold && p.Quantity > 0);
        int outOfStockCount = productProjections.Count(p => p.Quantity <= 0);

        foreach (var product in productProjections)
        {
            var totalCostValue = product.Quantity * product.CostPrice;
            var totalSaleValue = product.Quantity * product.SalePrice;
            decimal? potentialProfit = includeProfit ? totalSaleValue - totalCostValue : null;

            totalInventoryCost += totalCostValue;
            totalInventorySaleValue += totalSaleValue;

            var unitName = product.Unit switch
            {
                UnitType.Kilogram => "kg",
                UnitType.Meter    => "m",
                _                 => "dona",
            };

            inventoryReport.Add(new InventoryReportDto(
                product.Id,
                product.Name,
                product.Quantity,
                product.CostPrice,
                product.SalePrice,
                product.MinSalePrice,
                totalCostValue,
                totalSaleValue,
                potentialProfit,
                product.CategoryName,
                unitName
            ));
        }

        return new ComprehensiveReportDto(
            date,
            dailyReport,
            sellerReports,
            inventoryReport,
            totalInventoryCost,
            totalInventorySaleValue,
            productCount,
            totalInventoryCost,
            lowStockCount,
            outOfStockCount
        );
    }

    private static DailyReportDto CalculateReport(
        IEnumerable<Sale> sales,
        IEnumerable<Zakup> zakups,
        DateTime start,
        DateTime end,
        bool canViewProfit = false)
    {
        // ⭐ PROFESSIONAL VARIANT - Separate Paid and Debt sales
        decimal totalPaidSales = 0;      // To'langan savdolar
        decimal totalDebtSales = 0;      // Qarzga sotilgan
        decimal totalAllSales = 0;       // Jami savdo (paid + debt)
        decimal totalCost = 0;           // Cost of goods sold
        decimal totalProfit = 0;         // Actual profit from sales
        int totalTransactions = sales.Count();

        // Determine if profit should be included (Owner only)
        bool includeProfit = canViewProfit;

        // Calculate payment breakdown - separate positive and negative payments
        var paymentBreakdown = new Dictionary<string, decimal>();
        var paymentCounts = new Dictionary<string, int>();
        decimal totalRefunds = 0;  // Qaytarilgan summa

        // Calculate from sales and their items
        foreach (var sale in sales)
        {
            // IMPORTANT: Use sale.PaidAmount directly instead of summing payments
            // This ensures credit applications (which don't create payment records)
            // are not incorrectly counted in reports
            var paidAmount = sale.PaidAmount;
            var debtAmount = sale.TotalAmount - paidAmount;

            // Add to appropriate categories
            totalPaidSales += paidAmount;
            totalDebtSales += debtAmount;
            totalAllSales += sale.TotalAmount;

            // Calculate cost and profit from ALL sale items (both paid and debt)
            foreach (var item in sale.SaleItems)
            {
                // ✅ ISEXTERNAL SHARTI - EFFECTIVE COST PRICE
                decimal costPrice = item.IsExternal
                    ? item.ExternalCostPrice
                    : item.CostPrice;

                var itemCost = costPrice * item.Quantity;
                var itemRevenue = item.SalePrice * item.Quantity;
                var itemProfit = itemRevenue - itemCost;

                totalCost += itemCost;
                if (includeProfit)
                {
                    totalProfit += itemProfit;
                }
            }

            // Sale-level discount (chegirma) reduces profit once per sale.
            // Revenue (sale.TotalAmount) is already net of the discount, so
            // only profit — computed from GROSS item revenue above — needs it.
            if (includeProfit)
            {
                totalProfit -= sale.DiscountAmount;
            }

            // Accumulate payment breakdown from payments
            foreach (var payment in sale.Payments)
            {
                if (payment.Amount < 0)
                {
                    // Negative payment = refund/return.
                    // Manfiy Credit bundan MUSTASNO: u mijozning do'kondagi
                    // hisobiga qaytgan avans, kassadan chiqqan pul emas.
                    // Uni sanash «Возвраты» ni bo'lmagan qaytarishlar bilan
                    // shishirardi.
                    if (payment.PaymentType != PaymentType.Credit)
                        totalRefunds += Math.Abs(payment.Amount);
                }
                else
                {
                    // Positive payment = actual payment
                    var paymentType = payment.PaymentType.ToString();
                    if (!paymentBreakdown.ContainsKey(paymentType))
                    {
                        paymentBreakdown[paymentType] = 0;
                        paymentCounts[paymentType] = 0;
                    }
                    paymentBreakdown[paymentType] += payment.Amount;
                    paymentCounts[paymentType]++;
                }
            }
        }

        decimal totalZakup = zakups.Sum(z => z.Quantity * z.CostPrice);

        // Net income = Profit - Operating expenses (currently 0)
        decimal? netIncome = includeProfit ? totalProfit : null;
        decimal? profit = includeProfit ? totalProfit : null;

        // Convert to list of DTOs
        var paymentBreakdownList = paymentBreakdown
            .Select(kvp => new PaymentBreakdownDto(
                kvp.Key,
                kvp.Value,
                paymentCounts[kvp.Key]
            ))
            .ToList();

        // Add "Qarz" to payment breakdown if there is any debt sales
        if (totalDebtSales > 0)
        {
            paymentBreakdownList.Add(new PaymentBreakdownDto(
                "Qarz",
                totalDebtSales,
                0  // Count doesn't apply to debt
            ));
        }

        // Add "Qaytarilgan" to payment breakdown if there are any refunds
        if (totalRefunds > 0)
        {
            paymentBreakdownList.Add(new PaymentBreakdownDto(
                "Qaytarilgan",
                -totalRefunds,  // Show as negative to indicate deduction
                0  // Count doesn't apply to refunds
            ));
        }

        return new DailyReportDto(
            start,
            totalAllSales,
            totalPaidSales,
            totalDebtSales,
            totalZakup,
            profit,
            netIncome,
            totalTransactions,
            paymentBreakdownList
        );
    }

}
