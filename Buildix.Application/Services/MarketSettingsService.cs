using System.Globalization;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

public class MarketSettingsService : IMarketSettingsService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentMarketService _currentMarket;

    public MarketSettingsService(IAppDbContext db, ICurrentMarketService currentMarket)
    {
        _db = db;
        _currentMarket = currentMarket;
    }

    public async Task<MarketSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var settings = await GetOrCreateAsync(marketId, cancellationToken);
        return ToDto(settings);
    }

    public async Task<MarketSettingsDto> UpdateAsync(
        UpdateMarketSettingsRequest r,
        CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();
        var s = await GetOrCreateAsync(marketId, cancellationToken);

        s.Phone = Trim(r.Phone);
        s.Address = Trim(r.Address);
        s.WorkingHours = Trim(r.WorkingHours);
        s.SalesOnlyWhenShiftOpen = r.SalesOnlyWhenShiftOpen;
        s.CashWithdrawalNeedsApproval = r.CashWithdrawalNeedsApproval;
        s.DebtOnlyForRegulars = r.DebtOnlyForRegulars;
        s.DefaultDebtLimit = Math.Max(0m, r.DefaultDebtLimit);
        s.AllowedCashDiscrepancy = Math.Max(0m, r.AllowedCashDiscrepancy);
        s.ShiftAutoCloseTime = ParseTime(r.ShiftAutoCloseTime);
        // Davomat rejasi — noto'g'ri "HH:mm" kelsa joriy qiymat saqlanadi.
        s.WorkDayStart = ParseTime(r.WorkDayStart) ?? s.WorkDayStart;
        s.WorkDayEnd = ParseTime(r.WorkDayEnd) ?? s.WorkDayEnd;
        s.LateThreshold = ParseTime(r.LateThreshold) ?? s.LateThreshold;
        s.ReceiptHeader = Trim(r.ReceiptHeader);
        s.ReceiptFooter = Trim(r.ReceiptFooter);
        s.AutoPrintReceipt = r.AutoPrintReceipt;
        // Faqat ikki standart rulon eni. Boshqa qiymat kelsa 80 ga tushadi:
        // yaroqsiz en bilan chek qog'ozga sig'masdi va drayver uni o'zicha
        // siqib bosardi — har bir harf alohida qatorga tushardi.
        s.ReceiptWidthMm = r.ReceiptWidthMm <= 58 ? 58 : 80;

        // Yorliq rulonining o'lchami — chek enidan farqli, ro'yxat bilan
        // cheklab bo'lmaydi: 57×38, 57×30, 58×40, 40×30, 30×20 va boshqalar
        // bemalol uchraydi. Shuning uchun oraliqqa siqiladi.
        //
        // Yuqori chegara `PrintLabelsDto` dagi Range bilan bir xil (210×297):
        // ikkalasi bir xil qiymatni tekshiradi va ular ajralib ketmasligi
        // kerak. Quyi chegara — shtrix kod sig'adigan eng kichik yorliq.
        s.LabelWidthMm = Math.Clamp(r.LabelWidthMm, 20m, 210m);
        s.LabelHeightMm = Math.Clamp(r.LabelHeightMm, 15m, 297m);
        // Tirqish deyarli har doim 2 mm; 0 — uzluksiz (tirqishsiz) rulon.
        s.LabelGapMm = Math.Clamp(r.LabelGapMm, 0m, 20m);
        // Siljish MANFIY bo'lmaydi: TSPL `SHIFT` diapazoni 0…1016 va manfiy
        // son firmware tomonidan ishorasiz deb o'qilib, maketni bir necha
        // santimetrga sakratib yuboradi — haqiqiy printerda ko'rilgan.
        // Yuqori chegara yorliq bo'yidan oshmasin: aks holda maket butunlay
        // qo'shni yorliqqa ketardi.
        s.LabelOffsetMm = Math.Clamp(r.LabelOffsetMm, 0m, s.LabelHeightMm);

        s.DefaultLanguage = ParseLanguage(r.DefaultLanguage);
        s.FirstDayOfWeek = r.FirstDayOfWeek is >= 1 and <= 7 ? r.FirstDayOfWeek : 1;
        s.MinStockAlertEnabled = r.MinStockAlertEnabled;
        s.BlockSaleBelowCost = r.BlockSaleBelowCost;
        s.DefaultMarkupPct = Math.Clamp(r.DefaultMarkupPct, 0m, 1000m);
        s.NotifyDaySummary = r.NotifyDaySummary;
        s.NotifyOverdueDebts = r.NotifyOverdueDebts;
        s.NotifyWithdrawalRequests = r.NotifyWithdrawalRequests;
        s.InactivityLogoutMinutes = Math.Max(0, r.InactivityLogoutMinutes);
        s.AuditEnabled = r.AuditEnabled;

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(s);
    }

    public async Task<MarketSettings> GetOrCreateAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var settings = await _db.MarketSettings
            .FirstOrDefaultAsync(x => x.MarketId == marketId, cancellationToken);
        if (settings is not null) return settings;

        // Lazy-create with the entity's design defaults.
        settings = new MarketSettings { MarketId = marketId };
        _db.MarketSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private static MarketSettingsDto ToDto(MarketSettings s) => new(
        Phone: s.Phone,
        Address: s.Address,
        WorkingHours: s.WorkingHours,
        SalesOnlyWhenShiftOpen: s.SalesOnlyWhenShiftOpen,
        CashWithdrawalNeedsApproval: s.CashWithdrawalNeedsApproval,
        DebtOnlyForRegulars: s.DebtOnlyForRegulars,
        DefaultDebtLimit: s.DefaultDebtLimit,
        AllowedCashDiscrepancy: s.AllowedCashDiscrepancy,
        ShiftAutoCloseTime: s.ShiftAutoCloseTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
        WorkDayStart: s.WorkDayStart.ToString("HH:mm", CultureInfo.InvariantCulture),
        WorkDayEnd: s.WorkDayEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
        LateThreshold: s.LateThreshold.ToString("HH:mm", CultureInfo.InvariantCulture),
        ReceiptHeader: s.ReceiptHeader,
        ReceiptFooter: s.ReceiptFooter,
        AutoPrintReceipt: s.AutoPrintReceipt,
        ReceiptWidthMm: s.ReceiptWidthMm,
        LabelWidthMm: s.LabelWidthMm,
        LabelHeightMm: s.LabelHeightMm,
        LabelGapMm: s.LabelGapMm,
        LabelOffsetMm: s.LabelOffsetMm,
        DefaultLanguage: s.DefaultLanguage.ToCode(),
        FirstDayOfWeek: s.FirstDayOfWeek,
        MinStockAlertEnabled: s.MinStockAlertEnabled,
        BlockSaleBelowCost: s.BlockSaleBelowCost,
        DefaultMarkupPct: s.DefaultMarkupPct,
        NotifyDaySummary: s.NotifyDaySummary,
        NotifyOverdueDebts: s.NotifyOverdueDebts,
        NotifyWithdrawalRequests: s.NotifyWithdrawalRequests,
        InactivityLogoutMinutes: s.InactivityLogoutMinutes,
        AuditEnabled: s.AuditEnabled);

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // Nomarkaziy edi: "uz" dan boshqa hamma narsa (jumladan "en") Russian bo'lib
    // ketardi. Endi yagona LanguageCodes orqali; tanilmagan kodda o'zgarishsiz
    // qoldirish uchun chaqiruvchi standart qiymatni beradi.
    private static Language ParseLanguage(string? v) =>
        LanguageCodes.FromCode(v) ?? Language.Uzbek;

    private static TimeOnly? ParseTime(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        return TimeOnly.TryParseExact(v.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;
    }
}
