using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Extensions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Sales-list read models — role-filtered daily/range sales list, monthly
/// category breakdown, and detailed sales-with-items for export — extracted
/// verbatim from the former 2700-line ReportService.
/// </summary>
public sealed class SalesListService(
    IUnitOfWork unitOfWork,
    ICurrentMarketService currentMarketService,
    ITashkentClock clock)
    : ReportServiceBase(clock), ISalesListService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentMarketService _currentMarketService = currentMarketService;

    public async Task<DailySalesListDto> GetDailySalesListAsync(
        DateTime date,
        string? userRole = null,
        bool canViewProfit = false,
        Guid? userId = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        // Single day when endDate is null; otherwise the inclusive [date,
        // endDate] Tashkent-day range — start of the first day, end of the last.
        var (start, _) = GetUtcDateRange(date);
        var (_, end) = GetUtcDateRange(endDate ?? date);

        Expression<Func<Sale, bool>> salesQuery = s => s.CreatedAt >= start && s.CreatedAt < end &&
                              s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance &&
                              s.MarketId == marketId &&
                              (userRole != Role.Seller.ToString() || s.SellerId == userId);

        // SaleItems.Product ham yuklanadi — ro'yxatdagi "ТОВАРЫ" ustuni uchun
        // mahsulot nomi kerak (tashqi mahsulotda nom item'ning o'zida turadi).
        var sales = await _unitOfWork.Sales.FindAsync(
            salesQuery, q => q.Include(e => e.SaleItems).ThenInclude(i => i.Product).Include(e => e.Payments).Include(e => e.Seller).Include(e => e.Customer), cancellationToken);

        var salesListItems = new List<DailySalesListItemDto>();

        decimal totalPaidSales = 0;
        decimal totalDebtSales = 0;
        decimal totalAllSales = 0;

        bool includeProfit = canViewProfit;

        foreach (var sale in sales)
        {
            var paidAmount = sale.PaidAmount;
            var debtAmount = sale.TotalAmount - paidAmount;

            totalPaidSales += paidAmount;
            totalDebtSales += debtAmount;
            totalAllSales += sale.TotalAmount;

            decimal? profit = null;
            if (includeProfit)
            {
                var paidRatio = sale.TotalAmount > 0 ? paidAmount / sale.TotalAmount : 0;

                decimal grossProfit = 0;
                foreach (var item in sale.SaleItems)
                {
                    // ✅ ISEXTERNAL SHARTI - Effective cost price
                    decimal costPrice = item.IsExternal ? item.ExternalCostPrice : item.CostPrice;
                    var itemCost = costPrice * item.Quantity;
                    var itemRevenue = item.SalePrice * item.Quantity;

                    grossProfit += itemRevenue - itemCost;
                }

                // Chegirma (skidka) foydani sotuv bo'yicha BIR MARTA kamaytiradi:
                // yuqoridagi item foydasi GROSS tushumdan hisoblangan. paidRatio
                // esa allaqachon to'g'ri — sale.TotalAmount chegirilgan (net) summa.
                profit = (grossProfit - sale.DiscountAmount) * paidRatio;
            }

            // Check if this sale has any refund (negative) payments
            var hasRefunds = sale.Payments.Any(p => p.Amount < 0);

            // Determine payment type - if there are refunds, show as "Qaytarilgan"
            // Otherwise show the primary payment type
            string paymentType;
            if (hasRefunds)
            {
                paymentType = "Qaytarilgan";
            }
            else
            {
                var primaryPayment = sale.Payments.FirstOrDefault(p => p.Amount > 0);
                var paymentTypeRaw = primaryPayment?.PaymentType.ToString() ?? "Cash";
                paymentType = paymentTypeRaw.ToLowerInvariant();
            }

            // Nom: oddiy mahsulotda Product.Name, tashqi (bir martalik) mahsulotda
            // item'ga yozib qo'yilgan ExternalProductName.
            var lines = sale.SaleItems
                .Select(i => new DailySalesListLineDto(
                    (i.IsExternal ? i.ExternalProductName : i.Product?.Name) ?? string.Empty,
                    i.Quantity))
                .ToList();

            salesListItems.Add(new DailySalesListItemDto(
                sale.Id,
                sale.CreatedAt,
                sale.Seller?.FullName ?? "Unknown",
                sale.TotalAmount,
                paymentType,
                sale.Status.ToString(),
                profit,
                sale.Customer?.FullName,
                lines
            ));
        }

        decimal? summaryProfit = null;
        if (includeProfit && salesListItems.Any())
        {
            summaryProfit = salesListItems.Sum(s => s.Profit ?? 0);
        }

        return new DailySalesListDto(
            start,
            salesListItems,
            totalAllSales,     
            totalPaidSales,    
            totalDebtSales,    
            salesListItems.Count,
            summaryProfit
        );
    }

    public Task<MonthlyCategorySalesResponseDto> GetMonthlyCategorySalesAsync(DateTime date, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var start = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return GetCategorySalesAsync(start, start.AddMonths(1), canViewProfit, cancellationToken);
    }

    /// <summary>
    /// Kategoriyalar bo'yicha sotuv — IXTIYORIY davr uchun.
    /// </summary>
    /// <remarks>
    /// <para>Oy bo'yicha variant shu yerga tayanadi, ya'ni hisob mantig'i
    /// bitta joyda. «Kategoriyalar» bo'limi esa hafta/oy/chorak tanlashi
    /// mumkin — do'kon egasi «qaysi yo'nalish yaxshi ketyapti» degan savolga
    /// bir oy kutmasdan javob topishi kerak.</para>
    ///
    /// <para><b>Chegirma.</b> U butun chekka tegishli va uni bitta
    /// kategoriyaga bo'lib bo'lmaydi, shuning uchun kategoriya qatorlari
    /// chegirmasiz (gross) qoladi, yakuniy jami esa chegirma ayirilgan
    /// holda. Ulushni hisoblashda qatorlarning O'Z yig'indisi bo'luvchi
    /// bo'lishi kerak — aks holda foizlar 100 dan oshib ketadi.</para>
    /// </remarks>
    public async Task<MonthlyCategorySalesResponseDto> GetCategorySalesAsync(
        DateTime start, DateTime end, bool canViewProfit = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var date = start;

        var categories = await _unitOfWork.ProductCategories.FindAsync(
            c => c.MarketId == marketId && c.IsActive && !c.IsDeleted,
            cancellationToken);

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.CreatedAt >= start && s.CreatedAt < end && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft && !s.IsOpeningBalance && s.MarketId == marketId, q => q.Include(e => e.SaleItems), cancellationToken);

        var products = await _unitOfWork.Products.FindAsync(
            p => p.MarketId == marketId,
            cancellationToken);
        var productDict = products.ToDictionary(p => p.Id);

        bool includeProfit = canViewProfit;
        var categorySales = new Dictionary<int, CategorySalesDto>();

        foreach (var category in categories)
        {
            categorySales[category.Id] = new CategorySalesDto(category.Id, category.Name, 0, 0, includeProfit ? 0 : null);
        }

        int otherCategoryId = -1;
        categorySales[otherCategoryId] = new CategorySalesDto(otherCategoryId, "Boshqa", 0, 0, includeProfit ? 0 : null);

        decimal totalSalesOverall = 0;
        decimal totalProfitOverall = 0;

        foreach (var sale in sales)
        {
            foreach (var item in sale.SaleItems)
            {
                // ✅ ISEXTERNAL SHARTI - Product va CostPrice olish
                Product? product = null;
                int catId = otherCategoryId;

                if (!item.IsExternal && item.ProductId.HasValue)
                {
                    product = productDict.GetValueOrDefault(item.ProductId.Value);
                    catId = product?.CategoryId ?? otherCategoryId;
                }

                // Get or create category sales
                if (!categorySales.TryGetValue(catId, out var currentCat))
                {
                    catId = otherCategoryId;
                    currentCat = categorySales[catId];
                }

                decimal itemSales = item.Quantity * item.SalePrice;
                // ✅ Effective cost price
                var costPrice = item.IsExternal ? item.ExternalCostPrice : item.CostPrice;
                decimal itemProfit = (item.SalePrice - costPrice) * item.Quantity;

                decimal? newTotalProfit = includeProfit ? (currentCat.TotalProfit ?? 0) + itemProfit : null;

                categorySales[catId] = new CategorySalesDto(
                    currentCat.CategoryId,
                    currentCat.CategoryName,
                    currentCat.TotalSales + itemSales,
                    currentCat.TotalQuantity + item.Quantity,
                    newTotalProfit
                );

                totalSalesOverall += itemSales;
                if (includeProfit)
                {
                    totalProfitOverall += itemProfit;
                }
            }

            // Chegirma (skidka) sale darajasida — uni aniq bir kategoriyaga
            // bog'lab bo'lmaydi, shuning uchun per-category qatorlar GROSS qoladi.
            // Lekin YAKUNIY jami tushum va foyda NET bo'lishi shart, aks holda bu
            // hisobot dashboard'dagi ProfitSummary (net) bilan ziddiyatga tushardi.
            totalSalesOverall -= sale.DiscountAmount;
            if (includeProfit)
            {
                totalProfitOverall -= sale.DiscountAmount;
            }
        }

        if (categorySales[otherCategoryId].TotalSales == 0)
        {
            categorySales.Remove(otherCategoryId);
        }

        return new MonthlyCategorySalesResponseDto(
            date,
            categorySales.Values.ToList(),
            totalSalesOverall,
            includeProfit ? totalProfitOverall : null
        );
    }

}
