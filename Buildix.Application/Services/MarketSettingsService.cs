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

        // Changing the @username invalidates the cached chat_id link — the new
        // account must /start the bot again before notifications resume.
        var newTelegram = NormalizeTelegram(r.OwnerTelegram);
        if (!string.Equals(newTelegram, s.OwnerTelegram, StringComparison.OrdinalIgnoreCase))
        {
            s.OwnerTelegram = newTelegram;
            s.OwnerTelegramChatId = null;
        }

        s.UpdatedAt = DateTime.UtcNow;
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
        DefaultLanguage: s.DefaultLanguage.ToCode(),
        FirstDayOfWeek: s.FirstDayOfWeek,
        MinStockAlertEnabled: s.MinStockAlertEnabled,
        BlockSaleBelowCost: s.BlockSaleBelowCost,
        DefaultMarkupPct: s.DefaultMarkupPct,
        NotifyDaySummary: s.NotifyDaySummary,
        NotifyOverdueDebts: s.NotifyOverdueDebts,
        NotifyWithdrawalRequests: s.NotifyWithdrawalRequests,
        OwnerTelegram: s.OwnerTelegram,
        OwnerTelegramLinked: s.OwnerTelegramChatId.HasValue,
        InactivityLogoutMinutes: s.InactivityLogoutMinutes,
        AuditEnabled: s.AuditEnabled);

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? NormalizeTelegram(string? v)
    {
        var t = Trim(v);
        if (t is null) return null;
        return t.StartsWith('@') ? t : '@' + t;
    }

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
