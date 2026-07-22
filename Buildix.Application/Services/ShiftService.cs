using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Exceptions;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <inheritdoc cref="IShiftService"/>
public class ShiftService : IShiftService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _db;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IAuditLogService _auditLogService;
    private readonly IMarketSettingsService _settings;
    private readonly ITelegramNotifier _telegram;

    public ShiftService(
        IUnitOfWork unitOfWork,
        IAppDbContext db,
        ICurrentMarketService currentMarketService,
        IAuditLogService auditLogService,
        IMarketSettingsService settings,
        ITelegramNotifier telegram)
    {
        _unitOfWork = unitOfWork;
        _db = db;
        _currentMarketService = currentMarketService;
        _auditLogService = auditLogService;
        _settings = settings;
        _telegram = telegram;
    }

    public async Task<ShiftDto?> GetCurrentShiftAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var open = await FindOpenShiftAsync(userId, cancellationToken);
        if (open is null) return null;
        var fin = await ComputeFinancialsAsync(open, DateTime.UtcNow, cancellationToken);
        return ToDto(open, fin);
    }

    public async Task<ShiftDto> OpenShiftAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await FindOpenShiftAsync(userId, cancellationToken);
        if (existing is not null)
        {
            var f = await ComputeFinancialsAsync(existing, DateTime.UtcNow, cancellationToken);
            return ToDto(existing, f);
        }

        var marketId = _currentMarketService.GetCurrentMarketId();

        // Opening cash carries over from the last closed shift's counted amount
        // (the drawer isn't emptied between shifts). 0 on the very first shift.
        var lastCounted = await _db.Shifts
            .Where(s => s.MarketId == marketId && s.ClosedAt != null && s.CountedCash != null)
            .OrderByDescending(s => s.ClosedAt)
            .Select(s => s.CountedCash)
            .FirstOrDefaultAsync(cancellationToken);

        var shift = new Shift
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MarketId = marketId,
            OpenedAt = DateTime.UtcNow,
            OpeningCash = lastCounted ?? 0m,
            ReconStatus = CashShiftStatus.Open,
        };
        await _unitOfWork.Shifts.AddAsync(shift, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogActionAsync(
            AuditEntityTypes.Shift, shift.Id, AuditActions.Open, userId,
            new { shift.OpenedAt, shift.OpeningCash }, cancellationToken);

        var fin = await ComputeFinancialsAsync(shift, DateTime.UtcNow, cancellationToken);
        return ToDto(shift, fin);
    }

    public async Task<ShiftDto> CloseShiftAsync(Guid userId, decimal? countedCash = null, CancellationToken cancellationToken = default)
    {
        var open = await FindOpenShiftAsync(userId, cancellationToken)
            ?? throw new ShiftNotOpenException(userId);

        var closedAt = DateTime.UtcNow;
        var fin = await ComputeFinancialsAsync(open, closedAt, cancellationToken);

        open.ClosedAt = closedAt;
        if (countedCash.HasValue)
        {
            open.CountedCash = countedCash.Value;
            open.Discrepancy = countedCash.Value - fin.ExpectedCash;
            var settings = await _settings.GetOrCreateAsync(open.MarketId, cancellationToken);
            open.ReconStatus = Math.Abs(open.Discrepancy) <= settings.AllowedCashDiscrepancy
                ? CashShiftStatus.Balanced
                : CashShiftStatus.Discrepancy;
        }
        else
        {
            open.ReconStatus = CashShiftStatus.Balanced; // naqd sanalmagan — farq qayd etilmaydi
        }

        _unitOfWork.Shifts.Update(open);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogActionAsync(
            AuditEntityTypes.Shift, open.Id, AuditActions.Close, userId,
            new { open.OpenedAt, open.ClosedAt, open.DurationMinutes, open.CountedCash, open.Discrepancy, expected = fin.ExpectedCash },
            cancellationToken);

        // Day summary to the owner's Telegram (best-effort, gated by settings).
        var marketSettings = await _settings.GetOrCreateAsync(open.MarketId, cancellationToken);
        if (marketSettings.NotifyDaySummary)
        {
            var text =
                $"<b>Смена закрыта</b>\n" +
                $"Кассир: {open.User?.FullName ?? "—"}\n" +
                $"Чеков: {fin.CheckCount}\n" +
                $"Выручка: {fin.Revenue:N0} сум\n" +
                $"Наличными: {fin.CashIn:N0} · Картой: {fin.CardIn:N0}\n" +
                $"Расхождение: {open.Discrepancy:N0} сум";
            await _telegram.SendToOwnerAsync(open.MarketId, text, cancellationToken);
        }

        return ToDto(open, fin);
    }

    public async Task<IReadOnlyList<ShiftDto>> GetUserShiftsAsync(
        Guid userId, int limit = 30, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var shifts = await _db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.UserId == userId && s.MarketId == marketId)
            .OrderByDescending(s => s.OpenedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return await MapManyAsync(shifts, cancellationToken);
    }

    /// <summary>Market-wide shift history (all cashiers), most recent first.</summary>
    public async Task<IReadOnlyList<ShiftDto>> GetMarketShiftsAsync(int limit = 30, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var shifts = await _db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.MarketId == marketId)
            .OrderByDescending(s => s.OpenedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return await MapManyAsync(shifts, cancellationToken);
    }

    private async Task<IReadOnlyList<ShiftDto>> MapManyAsync(List<Shift> shifts, CancellationToken cancellationToken)
    {
        var result = new List<ShiftDto>(shifts.Count);
        foreach (var s in shifts)
        {
            var fin = await ComputeFinancialsAsync(s, s.ClosedAt ?? DateTime.UtcNow, cancellationToken);
            result.Add(ToDto(s, fin));
        }
        return result;
    }

    private record ShiftFinancials(decimal CashIn, decimal CardIn, decimal Withdrawals, decimal Revenue, int CheckCount, decimal ExpectedCash);

    /// <summary>Aggregates the money that moved through the drawer during a shift window.</summary>
    private async Task<ShiftFinancials> ComputeFinancialsAsync(Shift s, DateTime windowEnd, CancellationToken cancellationToken)
    {
        var marketId = s.MarketId;
        var start = s.OpenedAt;
        var seller = s.UserId;

        // H-11: attribute to THIS shift's cashier only. Without the seller filter,
        // two cashiers with concurrent open shifts on one market each counted the
        // other's sales/payments → phantom discrepancy + ~2x market totals.
        var payments = _db.Payments.AsNoTracking()
            .Where(p => p.Sale != null && p.Sale.MarketId == marketId && p.Sale.SellerId == seller
                && p.CreatedAt >= start && p.CreatedAt <= windowEnd);

        var cashIn = await payments.Where(p => p.PaymentType == PaymentType.Cash)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var cardIn = await payments.Where(p => p.PaymentType != PaymentType.Cash && p.PaymentType != PaymentType.Credit)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // H-10 + H-11: this cashier's own cash withdrawals, windowed by EFFECTIVE
        // cash-out time — an owner-approved request debits the till at approval
        // (ApprovedAt), not when it was requested (WithdrawalDate). NotRequired
        // rows have ApprovedAt = null → fall back to WithdrawalDate (immediate).
        var withdrawals = await _db.CashWithdrawals.AsNoTracking()
            .Where(w => w.MarketId == marketId && w.UserId == seller && w.WithdrawType == "cash"
                && (w.ApprovalStatus == WithdrawalApprovalStatus.NotRequired || w.ApprovalStatus == WithdrawalApprovalStatus.Approved)
                && (w.ApprovedAt ?? w.WithdrawalDate) >= start
                && (w.ApprovedAt ?? w.WithdrawalDate) <= windowEnd)
            .SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0m;

        var salesInWindow = _db.Sales.AsNoTracking()
            .Where(x => x.MarketId == marketId && x.SellerId == seller
                && x.CreatedAt >= start && x.CreatedAt <= windowEnd
                && x.Status != SaleStatus.Draft && x.Status != SaleStatus.Cancelled);
        var revenue = await salesInWindow.SumAsync(x => (decimal?)x.TotalAmount, cancellationToken) ?? 0m;
        var checkCount = await salesInWindow.CountAsync(cancellationToken);

        var expected = s.OpeningCash + cashIn - withdrawals;
        return new ShiftFinancials(cashIn, cardIn, withdrawals, revenue, checkCount, expected);
    }

    private async Task<Shift?> FindOpenShiftAsync(Guid userId, CancellationToken cancellationToken)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        return await _db.Shifts
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.MarketId == marketId && s.ClosedAt == null, cancellationToken);
    }

    private static ShiftDto ToDto(Shift s, ShiftFinancials fin) => new(
        s.Id, s.UserId, s.User?.FullName ?? "", s.OpenedAt, s.ClosedAt, s.IsOpen, s.DurationMinutes,
        s.OpeningCash, s.CountedCash, s.Discrepancy, s.ReconStatus.ToString(),
        fin.CheckCount, fin.Revenue, fin.CashIn, fin.CardIn, fin.Withdrawals, fin.ExpectedCash);
}
