using System.Globalization;
using Buildix.Application.Common;
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
    private readonly ITashkentClock _clock;
    private readonly ICashLedger _cashLedger;
    private readonly INotificationService _notifications;

    public ShiftService(
        IUnitOfWork unitOfWork,
        IAppDbContext db,
        ICurrentMarketService currentMarketService,
        IAuditLogService auditLogService,
        IMarketSettingsService settings,
        ITelegramNotifier telegram,
        ITashkentClock clock,
        ICashLedger cashLedger,
        INotificationService notifications)
    {
        _unitOfWork = unitOfWork;
        _db = db;
        _currentMarketService = currentMarketService;
        _auditLogService = auditLogService;
        _settings = settings;
        _telegram = telegram;
        _clock = clock;
        _cashLedger = cashLedger;
        _notifications = notifications;
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

        // Смена № is customer-facing (it prints on the receipt), so allocate it
        // under the same per-market advisory lock the ЧЕК № uses: two cashiers
        // opening at once serialise here instead of computing the same max+1.
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await MarketSequenceLock.AcquireAsync(
                _db, MarketSequenceLock.ShiftNumberClass, marketId, cancellationToken);

            shift.ShiftNumber = (await _db.Shifts
                .Where(s => s.MarketId == marketId)
                .MaxAsync(s => (int?)s.ShiftNumber, cancellationToken) ?? 0) + 1;

            await _unitOfWork.Shifts.AddAsync(shift, cancellationToken);

            // Касса jurnaliga «Открытие» — smena boshidagi qoldiq (dizayndagi
            // birinchi qator). Bu yangi pul emas (oldingi smenadan qolgan), shuning
            // uchun Приход aggregatidan chiqariladi (Opening turi bo'yicha). 0 bo'lsa
            // yozilmaydi. shiftId — shu smena.
            _cashLedger.Record(marketId, shift.OpeningCash, CashMovementType.Opening,
                userId: userId, shiftId: shift.Id,
                comment: $"Остаток на открытие смены С-{shift.ShiftNumber}");

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

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

        // Self-close: the audit actor is the shift's own cashier.
        return await CloseShiftCoreAsync(open, actorId: userId, countedCash, forced: false, cancellationToken);
    }

    public async Task<ShiftDto> ForceCloseShiftAsync(Guid shiftId, Guid closedByUserId, decimal? countedCash = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var open = await _db.Shifts
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == shiftId && s.MarketId == marketId && s.ClosedAt == null, cancellationToken)
            ?? throw new InvalidOperationException("Ochiq smena topilmadi yoki allaqachon yopilgan.");

        // Force-close: the audit actor is the Owner/Admin, NOT the cashier —
        // so the trail shows who intervened.
        return await CloseShiftCoreAsync(open, actorId: closedByUserId, countedCash, forced: true, cancellationToken);
    }

    /// <summary>
    /// Shared close logic for both self-close and force-close. Reconciles the
    /// drawer, stamps ClosedAt, writes the audit row (with <paramref name="forced"/>
    /// and the acting user), and sends the day-summary Telegram.
    /// </summary>
    private async Task<ShiftDto> CloseShiftCoreAsync(Shift open, Guid actorId, decimal? countedCash, bool forced, CancellationToken cancellationToken)
    {
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
            AuditEntityTypes.Shift, open.Id, AuditActions.Close, actorId,
            new { open.OpenedAt, open.ClosedAt, open.DurationMinutes, open.CountedCash, open.Discrepancy, expected = fin.ExpectedCash, forced, cashierId = open.UserId },
            cancellationToken);

        // In-app bildirishnoma: smena yopildi; kassa farqi bo'lsa — ogohlantirish.
        var cashier = open.User?.FullName ?? "Кассир";
        if (open.ReconStatus == CashShiftStatus.Discrepancy)
            await _notifications.RecordAsync(open.MarketId, NotificationCategory.Shift, NotificationSeverity.Danger,
                "Расхождение кассы", $"Смена С-{open.ShiftNumber} · {cashier} · {open.Discrepancy:N0} сум", "shifts",
                cancellationToken: cancellationToken);
        else
            await _notifications.RecordAsync(open.MarketId, NotificationCategory.Shift, NotificationSeverity.Success,
                "Смена закрыта", $"Смена С-{open.ShiftNumber} · {cashier} · выручка {fin.Revenue:N0} сум", "shifts",
                cancellationToken: cancellationToken);

        // Day summary to the owner's Telegram (best-effort, gated by settings).
        // Additionally honour the recipient's per-user toggle (BE-9): suppress
        // only if an Owner explicitly turned "Закрытие смены" off — default true
        // keeps the existing behaviour when no one opted out.
        var marketSettings = await _settings.GetOrCreateAsync(open.MarketId, cancellationToken);
        var ownerMutedShift = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.MarketId == open.MarketId && u.Role == Role.Owner && !u.NotifyShift, cancellationToken);
        if (marketSettings.NotifyDaySummary && !ownerMutedShift)
        {
            var text =
                $"<b>Смена закрыта</b>\n" +
                $"Кассир: {open.User?.FullName ?? "—"}\n" +
                $"Чеков: {fin.CheckCount}\n" +
                $"Выручка: {fin.Revenue:N0} сум\n" +
                $"Наличными: {fin.CashIn:N0} · Терминал: {fin.TerminalIn:N0} · Click: {fin.ClickIn:N0}\n" +
                // Credit sold on this shift and returns are part of how the shift
                // closed — without them the owner saw revenue that never became
                // money and could not tell a refunded receipt from a missing one.
                $"В долг: {fin.DebtIn:N0} сум ({fin.DebtCount})\n" +
                (fin.ReturnCount > 0 ? $"Возвратов: {fin.ReturnAmount:N0} сум ({fin.ReturnCount})\n" : "") +
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

    /// <summary>Market-wide shift history (all cashiers), most recent first,
    /// with optional cashier and UTC date-range filters.</summary>
    public async Task<IReadOnlyList<ShiftDto>> GetMarketShiftsAsync(int limit = 30, Guid? userId = null, DateTime? fromUtc = null, DateTime? toUtc = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var query = _db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.MarketId == marketId);

        if (userId.HasValue) query = query.Where(s => s.UserId == userId.Value);
        if (fromUtc.HasValue) query = query.Where(s => s.OpenedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(s => s.OpenedAt < toUtc.Value);

        var shifts = await query
            .OrderByDescending(s => s.OpenedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);
        return await MapManyAsync(shifts, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MyShiftsDto> GetMyShiftsAsync(
        Guid userId, string? range = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Anchor the period to the Tashkent business day like every other dated
        // query — "this week" must not slide by the UTC offset.
        DateTime? fromUtc = (range ?? "week").ToLowerInvariant() switch
        {
            "all" => null,
            "month" => _clock.LocalDayToUtcRange(_clock.TodayLocal.AddDays(-29)).UtcStart,
            _ => _clock.LocalDayToUtcRange(_clock.TodayLocal.AddDays(-6)).UtcStart,
        };

        var query = _db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.UserId == userId && s.MarketId == marketId);
        if (fromUtc is { } from)
            query = query.Where(s => s.OpenedAt >= from);

        var shifts = await query
            .OrderByDescending(s => s.OpenedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var items = await MapManyAsync(shifts, cancellationToken);
        var totalRevenue = items.Sum(i => i.Revenue);
        var totalChecks = items.Sum(i => i.CheckCount);
        var avgCheck = totalChecks > 0 ? Math.Round(totalRevenue / totalChecks, 2) : 0m;
        return new MyShiftsDto(items, totalRevenue, totalChecks, avgCheck);
    }

    public async Task<AttendanceDto> GetAttendanceAsync(string? range = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Do'kon ish grafigi endi sozlanadi (Настройки → Посещаемость). Standart
        // 08:00–20:00 · kech 08:15 — mavjud xatti-harakat.
        var settings = await _settings.GetOrCreateAsync(marketId, cancellationToken);
        var scheduleStart = settings.WorkDayStart.ToTimeSpan();
        var scheduleEnd = settings.WorkDayEnd.ToTimeSpan();
        var lateThreshold = settings.LateThreshold.ToTimeSpan();
        var planHoursPerDay = (decimal)(scheduleEnd - scheduleStart).TotalHours;

        // Trailing Tashkent-day window, same anchoring as GetMyShiftsAsync so the
        // two Смены tabs cover the same period. "all" isn't offered — a plan %
        // needs a bounded horizon.
        var normalized = (range ?? "month").ToLowerInvariant();
        var windowDays = normalized == "week" ? 7 : 30;
        var period = normalized == "week" ? "week" : "month";
        var fromUtc = _clock.LocalDayToUtcRange(_clock.TodayLocal.AddDays(-(windowDays - 1))).UtcStart;

        var shifts = await _db.Shifts.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.MarketId == marketId && s.OpenedAt >= fromUtc)
            .ToListAsync(cancellationToken);

        // Plan "to this day" — every calendar day in the window counts (retail
        // works 7 days a week), each worth a full scheduled day.
        var planHours = Math.Round(windowDays * planHoursPerDay, 1);

        var items = shifts
            .GroupBy(s => s.UserId)
            .Select(g =>
            {
                var totalHours = (decimal)g.Sum(s => s.DurationMinutes) / 60m;
                var shiftCount = g.Count();
                // Distinct Tashkent calendar days worked — two shifts in one day count once.
                var dayCount = g.Select(s => _clock.ToLocal(s.OpenedAt).Date).Distinct().Count();
                // Kechikish — smena kechikish chegarasidan keyin ochilgan bo'lsa (Tashkent vaqti).
                var lateCount = g.Count(s => _clock.ToLocal(s.OpenedAt).TimeOfDay > lateThreshold);
                return new AttendanceRowDto(
                    g.Key,
                    g.First().User?.FullName ?? "",
                    shiftCount,
                    dayCount,
                    Math.Round(totalHours, 1),
                    Math.Round(shiftCount > 0 ? totalHours / shiftCount : 0m, 1),
                    lateCount);
            })
            .OrderByDescending(r => r.TotalHours)
            .ToList();

        return new AttendanceDto(
            period,
            settings.WorkDayStart.ToString("HH:mm", CultureInfo.InvariantCulture),
            settings.WorkDayEnd.ToString("HH:mm", CultureInfo.InvariantCulture),
            settings.LateThreshold.ToString("HH:mm", CultureInfo.InvariantCulture),
            planHours,
            items);
    }

    private async Task<IReadOnlyList<ShiftDto>> MapManyAsync(List<Shift> shifts, CancellationToken cancellationToken)
    {
        if (shifts.Count == 0) return [];

        var now = DateTime.UtcNow;
        var financials = await ComputeFinancialsAsync(
            shifts.Select(s => (s, s.ClosedAt ?? now)).ToList(), cancellationToken);

        return shifts.Select(s => ToDto(s, financials[s.Id])).ToList();
    }

    private record ShiftFinancials(
        decimal CashIn, decimal CardIn, decimal Withdrawals, decimal Revenue, int CheckCount, decimal ExpectedCash,
        decimal DebtIn, int CashCount, int CardCount, int DebtCount, decimal ReturnAmount, int ReturnCount,
        decimal TerminalIn, decimal ClickIn, int TerminalCount, int ClickCount,
        decimal ExternalPayouts);

    /// <summary>
    /// Bitta smenaning moliyasi. Guruhli hisobning ustiga qurilgan, ya'ni
    /// mantiq YAGONA joyda turadi.
    /// </summary>
    private async Task<ShiftFinancials> ComputeFinancialsAsync(
        Shift s, DateTime windowEnd, CancellationToken cancellationToken)
        => (await ComputeFinancialsAsync([(s, windowEnd)], cancellationToken))[s.Id];

    /// <summary>
    /// Bir nechta smenaning moliyasini TO'RTTA so'rovda hisoblaydi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nimadan qutuladi.</b> Ilgari har bir smena uchun o'n beshta
    /// ketma-ket so'rov bajarilardi va ular halqa ichida edi. «Smenalar»
    /// ro'yxati ellikta smenani ko'rsatsa — 750 ta so'rov. Panel esa shu
    /// ro'yxatni HAR DAQIQADA qayta so'raydi (faqat «nechta sotuvchi
    /// smenada» degan raqam uchun), ya'ni ochiq turgan panel bazani
    /// daqiqasiga 750 marta bezovta qilardi.</para>
    ///
    /// <para><b>Nega xotirada guruhlanadi.</b> Smena oynasi (sotuvchi +
    /// vaqt oralig'i) oddiy kalit emas — uni SQL join'ga aylantirish uchun
    /// xom so'rov kerak bo'lardi va u faqat PostgreSQL'da ishlardi
    /// (sinovlar esa xotiradagi provayderda ketadi). Shuning uchun bazadan
    /// faqat kerakli USTUNLAR tortiladi va taqsimlash shu yerda bajariladi:
    /// qatorlar kichik va ular baribir yig'ilishi kerak edi.</para>
    ///
    /// <para>Hisob-kitob qoidalari o'zgarmagan — izohlar ilgari har bir
    /// so'rov yonida turgan edi.</para>
    /// </remarks>
    private async Task<Dictionary<Guid, ShiftFinancials>> ComputeFinancialsAsync(
        IReadOnlyList<(Shift Shift, DateTime WindowEnd)> windows, CancellationToken cancellationToken)
    {
        var markets = windows.Select(w => w.Shift.MarketId).Distinct().ToList();
        var sellers = windows.Select(w => w.Shift.UserId).Distinct().ToList();
        var from = windows.Min(w => w.Shift.OpenedAt);
        var to = windows.Max(w => w.WindowEnd);

        // ── To'lovlar ────────────────────────────────────────────────────
        // H-11: pul KIM YIG'GANiga qarab taqsimlanadi. CollectedByUserId
        // qarz keyinroq to'langanda qo'yiladi (boshqa kassir bo'lishi
        // mumkin) va odatdagi kassadagi to'lovda NULL bo'ladi — o'shanda
        // sotuvning o'z sotuvchisi olgan. Busiz B yig'gan pul A ning
        // smenasiga tushardi.
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.Sale != null && markets.Contains(p.Sale.MarketId)
                && p.CreatedAt >= from && p.CreatedAt <= to
                && sellers.Contains(p.CollectedByUserId ?? p.Sale.SellerId))
            .Select(p => new PaymentRow(
                p.CollectedByUserId ?? p.Sale!.SellerId, p.SaleId, p.PaymentType, p.Amount, p.CreatedAt))
            .ToListAsync(cancellationToken);

        // ── Naqd yechish ─────────────────────────────────────────────────
        // H-10: oyna EFFEKTIV chiqish vaqti bo'yicha — tasdiq talab
        // qiladigan so'rov kassadan tasdiqlanganda chiqadi (ApprovedAt),
        // so'ralganda emas.
        var withdrawals = await _db.CashWithdrawals.AsNoTracking()
            .Where(w => markets.Contains(w.MarketId) && w.UserId != null && sellers.Contains(w.UserId.Value) && w.WithdrawType == "cash"
                && (w.ApprovalStatus == WithdrawalApprovalStatus.NotRequired
                    || w.ApprovalStatus == WithdrawalApprovalStatus.Approved)
                && (w.ApprovedAt ?? w.WithdrawalDate) >= from
                && (w.ApprovedAt ?? w.WithdrawalDate) <= to)
            .Select(w => new MovementRow(w.UserId!.Value, w.Amount, w.ApprovedAt ?? w.WithdrawalDate))
            .ToListAsync(cancellationToken);

        var sales = await _db.Sales.AsNoTracking()
            .Where(x => markets.Contains(x.MarketId) && sellers.Contains(x.SellerId)
                && x.CreatedAt >= from && x.CreatedAt <= to
                && x.Status != SaleStatus.Draft && x.Status != SaleStatus.Cancelled && !x.IsOpeningBalance)
            .Select(x => new SaleRow(
                x.Id, x.SellerId, x.CreatedAt, x.TotalAmount, x.PaidAmount, x.Status))
            .ToListAsync(cancellationToken);

        // Qo'shni do'kondan olingan tovar puli — sotuvning O'ZIDAN, kassa
        // jurnalidan emas (jurnal hisob-kitob manbai emas). `sales`
        // allaqachon Draft/Cancelled ni chiqarib tashlagan.
        var saleIds = sales.Select(x => x.Id).ToList();
        var externalBySale = saleIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : (await _db.SaleItems.AsNoTracking()
                .Where(si => si.IsExternal && saleIds.Contains(si.SaleId))
                .GroupBy(si => si.SaleId)
                .Select(g => new ExternalRow(g.Key, g.Sum(si => si.ExternalCostPrice * si.Quantity)))
                .ToListAsync(cancellationToken))
              .ToDictionary(x => x.SaleId, x => x.Cost);

        var result = new Dictionary<Guid, ShiftFinancials>(windows.Count);
        foreach (var (shift, windowEnd) in windows)
            result[shift.Id] = Fold(shift, windowEnd, payments, withdrawals, sales, externalBySale);
        return result;
    }

    private readonly record struct PaymentRow(
        Guid Seller, Guid SaleId, PaymentType Type, decimal Amount, DateTime At);

    private readonly record struct MovementRow(Guid Seller, decimal Amount, DateTime At);

    private readonly record struct SaleRow(
        Guid Id, Guid Seller, DateTime At, decimal Total, decimal Paid, SaleStatus Status);

    private readonly record struct ExternalRow(Guid SaleId, decimal Cost);

    /// <summary>Bitta smena oynasiga tushgan qatorlarni yig'adi.</summary>
    private static ShiftFinancials Fold(
        Shift s, DateTime windowEnd,
        List<PaymentRow> allPayments, List<MovementRow> allWithdrawals,
        List<SaleRow> allSales, Dictionary<Guid, decimal> externalBySale)
    {
        bool InWindow(DateTime at) => at >= s.OpenedAt && at <= windowEnd;

        var mine = allPayments.Where(p => p.Seller == s.UserId && InWindow(p.At)).ToList();

        // Yig'indilar NET qoladi (qaytarish — manfiy to'lov), chunki
        // ExpectedCash — kassada JISMONAN turishi kerak bo'lgan pul.
        // Quyidagi qaytarish raqamlari esa faqat ko'rsatish uchun.
        var cashIn = mine.Where(p => p.Type == PaymentType.Cash).Sum(p => p.Amount);

        // Naqdsiz — Click ham shu ichida. CardIn ATAYLAB to'liq naqdsiz
        // yig'indi bo'lib qoladi (Flutter mijozi uni «Karta» deb o'qiydi),
        // TerminalIn/ClickIn esa uning ichki bo'linishi.
        var cardIn = mine
            .Where(p => p.Type != PaymentType.Cash && p.Type != PaymentType.Credit)
            .Sum(p => p.Amount);
        var clickIn = mine.Where(p => p.Type == PaymentType.Click).Sum(p => p.Amount);
        var terminalIn = cardIn - clickIn;

        // Chek soni tur bo'yicha — faqat MUSBAT harakatlar (qaytarish yangi
        // chek emas). Aralash chek ham Terminal, ham Click qatoriga ega
        // bo'lishi mumkin, shuning uchun ular alohida sanaladi.
        int Checks(Func<PaymentRow, bool> match) =>
            mine.Where(p => p.Amount > 0 && match(p)).Select(p => p.SaleId).Distinct().Count();

        var cashCount = Checks(p => p.Type == PaymentType.Cash);
        var cardCount = Checks(p => p.Type != PaymentType.Cash && p.Type != PaymentType.Credit);
        var clickCount = Checks(p => p.Type == PaymentType.Click);
        var terminalCount = Checks(p => p.Type is PaymentType.Terminal or PaymentType.Transfer);

        var refunds = mine.Where(p => p.Amount < 0).ToList();
        var returnAmount = -refunds.Sum(p => p.Amount);
        var returnCount = refunds.Select(p => p.SaleId).Distinct().Count();

        var withdrawals = allWithdrawals
            .Where(w => w.Seller == s.UserId && InWindow(w.At))
            .Sum(w => w.Amount);

        var salesInWindow = allSales
            .Where(x => x.Seller == s.UserId && InWindow(x.At))
            .ToList();

        var revenue = salesInWindow.Sum(x => x.Total);
        var checkCount = salesInWindow.Count;

        // Shu smena tabga yozgan va HALI qaytmagan qarz. Joriy holat
        // o'qiladi, ya'ni keyinroq to'liq to'langan qarz bu yerda
        // sanalmaydi — undan yig'ilgan naqd yig'gan smenaga tushadi.
        var debtSales = salesInWindow.Where(x => x.Status == SaleStatus.Debt).ToList();
        var debtIn = debtSales.Sum(x => x.Total - x.Paid);
        var debtCount = debtSales.Count;

        var externalPayouts = salesInWindow
            .Sum(x => externalBySale.TryGetValue(x.Id, out var cost) ? cost : 0m);

        var expected = s.OpeningCash + cashIn - withdrawals - externalPayouts;
        return new ShiftFinancials(
            cashIn, cardIn, withdrawals, revenue, checkCount, expected,
            debtIn, cashCount, cardCount, debtCount, returnAmount, returnCount,
            terminalIn, clickIn, terminalCount, clickCount, externalPayouts);
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
        fin.CheckCount, fin.Revenue, fin.CashIn, fin.CardIn, fin.Withdrawals, fin.ExpectedCash,
        fin.DebtIn, fin.CashCount, fin.CardCount, fin.DebtCount, fin.ReturnAmount, fin.ReturnCount,
        s.ShiftNumber,
        fin.TerminalIn, fin.ClickIn, fin.TerminalCount, fin.ClickCount, fin.ExternalPayouts);
}
