using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

public class CashRegisterService : ICashRegisterService
{
    private const string WithdrawTypeCash = "cash";
    private const string WithdrawTypeClick = "click";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CashRegisterService> _logger;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly ITashkentClock _clock;
    private readonly IAuditLogService _auditLogService;
    private readonly IMarketSettingsService _settings;
    private readonly ITelegramNotifier _telegram;
    private readonly ICashLedger _cashLedger;

    public CashRegisterService(IUnitOfWork unitOfWork, ILogger<CashRegisterService> logger, IAppDbContext context, ICurrentMarketService currentMarketService, ITashkentClock clock, IAuditLogService auditLogService, IMarketSettingsService settings, ITelegramNotifier telegram, ICashLedger cashLedger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _context = context;
        _currentMarketService = currentMarketService;
        _clock = clock;
        _auditLogService = auditLogService;
        _settings = settings;
        _telegram = telegram;
        _cashLedger = cashLedger;
    }

    private async Task<Role?> GetRoleAsync(Guid userId, CancellationToken ct) =>
        await _context.Users.Where(u => u.Id == userId).Select(u => (Role?)u.Role).FirstOrDefaultAsync(ct);

    public async Task<CashLedgerDto> GetCashLedgerAsync(DateTime? localDate, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var day = localDate?.Date ?? _clock.TodayLocal;
        var (utcStart, utcEnd) = _clock.LocalDayToUtcRange(day);

        var balance = await _context.CashRegisters
            .Where(cr => cr.MarketId == marketId)
            .Select(cr => (decimal?)cr.CurrentBalance)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

        var movements = await _context.CashMovements
            .AsNoTracking()
            .Where(m => m.MarketId == marketId && m.CreatedAt >= utcStart && m.CreatedAt < utcEnd)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new CashMovementDto(
                m.Id,
                m.Type.ToString(),
                m.Amount,
                m.Category,
                m.RefNumber,
                m.User != null ? m.User.FullName : null,
                m.Comment,
                m.CreatedAt))
            .ToListAsync(cancellationToken);

        // Приход/Расход — Opening (boshlang'ich qoldiq) tushum/chiqim emas,
        // shuning uchun aggregatlardan chiqariladi (dizayn uni alohida ko'rsatadi).
        var income = movements.Where(m => m.Amount > 0 && m.Type != nameof(CashMovementType.Opening)).ToList();
        var expense = movements.Where(m => m.Amount < 0).ToList();

        return new CashLedgerDto(
            balance,
            income.Sum(m => m.Amount),
            expense.Sum(m => m.Amount),
            income.Count,
            expense.Count,
            movements);
    }

    private async Task<CashRegister> GetOrCreateRegisterAsync(int marketId, CancellationToken cancellationToken)
    {
        var register = await _context.CashRegisters
            .Include(x => x.LastWithdrawal)
            .FirstOrDefaultAsync(x => x.MarketId == marketId, cancellationToken);

        if (register != null) return register;

        register = new CashRegister
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            CurrentBalance = 0,
            LastUpdated = DateTime.UtcNow
        };
        _context.CashRegisters.Add(register);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return register;
        }
        catch (DbUpdateException)
        {
            // Another concurrent request just inserted the per-market register.
            // Drop the local entity from the change tracker and re-read.
            _context.Entry(register).State = EntityState.Detached;
            var existing = await _context.CashRegisters
                .Include(x => x.LastWithdrawal)
                .FirstOrDefaultAsync(x => x.MarketId == marketId, cancellationToken);
            if (existing == null) throw;
            return existing;
        }
    }

    public async Task<CashRegisterDto?> GetCashRegisterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var marketId = _currentMarketService.GetCurrentMarketId();
            var cashRegister = await GetOrCreateRegisterAsync(marketId, cancellationToken);

            // K2: filter by CashWithdrawal.MarketId directly. The previous
            // query joined to User and accepted UserId=null rows as "ok",
            // which leaked every other tenant's orphaned withdrawals (e.g.
            // after a user is hard-deleted) into this market's list.
            // M-3: only ACTUAL cash-outs belong in the recent-withdrawals list —
            // a Pending request hasn't left the till and a Rejected one never
            // will. Without this filter the list (and the dashboard "withdrawals
            // today" hint) shows money that is still in the drawer.
            var withdrawals = await _context.CashWithdrawals
                .Include(x => x.User)
                .Where(x => x.MarketId == marketId
                    && (x.ApprovalStatus == WithdrawalApprovalStatus.NotRequired
                        || x.ApprovalStatus == WithdrawalApprovalStatus.Approved))
                .OrderByDescending(x => x.WithdrawalDate)
                .Take(50)
                .ToListAsync(cancellationToken);

            return new CashRegisterDto
            {
                Id = cashRegister.Id,
                CurrentBalance = cashRegister.CurrentBalance,
                LastUpdated = cashRegister.LastUpdated,
                Withdrawals = withdrawals.Select(w => new CashWithdrawalDto
                {
                    Id = w.Id,
                    Amount = w.Amount,
                    Comment = w.Comment,
                    WithdrawalDate = w.WithdrawalDate,
                    UserName = w.User?.FullName ?? "Unknown"
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cash register");
            return null;
        }
    }

    public async Task<Result<bool>> WithdrawCashAsync(WithdrawCashRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var marketId = _currentMarketService.GetCurrentMarketId();

            // Approval gate: when the market requires owner approval, an Admin
            // cannot withdraw directly — they must submit a request. Owner/
            // SuperAdmin may always withdraw immediately.
            var settings = await _settings.GetOrCreateAsync(marketId, cancellationToken);
            if (settings.CashWithdrawalNeedsApproval)
            {
                var role = await GetRoleAsync(userId, cancellationToken);
                if (role == Role.Admin)
                    return Result.Failure<bool>(
                        "Требуется одобрение владельца. Отправьте запрос на снятие.", "NEEDS_APPROVAL");
            }

            CashWithdrawal? recorded = null;

            var ok = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var cashRegister = await _context.CashRegisters
                    .FirstOrDefaultAsync(x => x.MarketId == marketId, cancellationToken);

                if (cashRegister == null)
                {
                    _logger.LogWarning("Cash register not found for market {MarketId}", marketId);
                    return false;
                }

                if (request.Amount <= 0)
                {
                    _logger.LogWarning("Invalid withdrawal amount: {Amount}", request.Amount);
                    return false;
                }

                _logger.LogInformation("WithdrawCashAsync called. Type: {Type}, Amount: {Amount}, MarketId: {MarketId}",
                    request.WithdrawType, request.Amount, marketId);

                if (request.WithdrawType == WithdrawTypeCash)
                {
                    if (cashRegister.CurrentBalance < request.Amount)
                    {
                        _logger.LogWarning("Insufficient funds. Balance: {Balance}, Requested: {Amount}",
                            cashRegister.CurrentBalance, request.Amount);
                        return false;
                    }

                    var withdrawal = new CashWithdrawal
                    {
                        Id = Guid.NewGuid(),
                        Amount = request.Amount,
                        Comment = request.Comment,
                        WithdrawalDate = DateTime.UtcNow,
                        UserId = userId,
                        WithdrawType = WithdrawTypeCash,
                        MarketId = marketId
                    };

                    _context.CashWithdrawals.Add(withdrawal);

                    cashRegister.CurrentBalance -= request.Amount;
                    cashRegister.LastUpdated = DateTime.UtcNow;
                    cashRegister.LastWithdrawalId = withdrawal.Id;

                    // Касса jurnaliga chiqim. «Новая операция» Инкассация yoki
                    // kategoriyali Расход yuborishi mumkin (aks holda oddiy Расход).
                    _cashLedger.Record(marketId,
                        -request.Amount,
                        request.IsCollection ? CashMovementType.Collection : CashMovementType.Expense,
                        userId: userId,
                        category: request.IsCollection ? null : request.Category,
                        comment: request.Comment);

                    await _context.SaveChangesAsync(cancellationToken);
                    recorded = withdrawal;
                    _logger.LogInformation("Cash withdrawn. Amount: {Amount}", request.Amount);
                }
                else if (request.WithdrawType == WithdrawTypeClick)
                {
                    var withdrawal = new CashWithdrawal
                    {
                        Id = Guid.NewGuid(),
                        Amount = request.Amount,
                        Comment = request.Comment,
                        WithdrawalDate = DateTime.UtcNow,
                        UserId = userId,
                        WithdrawType = WithdrawTypeClick,
                        MarketId = marketId
                    };

                    _context.CashWithdrawals.Add(withdrawal);

                    cashRegister.LastUpdated = DateTime.UtcNow;
                    cashRegister.LastWithdrawalId = withdrawal.Id;

                    await _context.SaveChangesAsync(cancellationToken);
                    recorded = withdrawal;
                    _logger.LogInformation("Click withdrawal recorded. Amount: {Amount}", request.Amount);
                }
                else
                {
                    _logger.LogWarning("Invalid withdraw type: {Type}", request.WithdrawType);
                    return false;
                }

                return true;
            }, cancellationToken);

            // Audit AFTER the transaction commits. LogActionAsync swallows its
            // own errors, but running it outside the transaction also guarantees
            // a failed audit write can never roll back a completed withdrawal.
            if (ok && recorded is not null)
            {
                await _auditLogService.LogActionAsync(
                    AuditEntityTypes.CashRegister, recorded.Id, AuditActions.Withdraw, userId,
                    new { recorded.Amount, recorded.WithdrawType, recorded.Comment }, cancellationToken);
            }

            return ok
                ? Result.Success(true)
                : Result.Failure<bool>("Не удалось снять наличные. Проверьте баланс или сумму.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error withdrawing cash");
            return Result.Failure<bool>("Ошибка при снятии наличных.");
        }
    }

    public async Task<Result<bool>> RequestWithdrawalAsync(WithdrawCashRequest request, Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Result.Failure<bool>("Summa 0 dan katta bo'lishi kerak.");

        var marketId = _currentMarketService.GetCurrentMarketId();

        // Pending request — records intent but does NOT touch the balance. The
        // balance is only debited when the owner approves.
        // L-2: honour the requested type (cash/click) instead of hardcoding cash —
        // ApproveWithdrawalAsync debits the till only for 'cash', so a mislabelled
        // 'click' request would wrongly reduce the balance on approval.
        var withdrawType = request.WithdrawType == WithdrawTypeClick ? WithdrawTypeClick : WithdrawTypeCash;
        var withdrawal = new CashWithdrawal
        {
            Id = Guid.NewGuid(),
            Amount = request.Amount,
            Comment = request.Comment,
            WithdrawalDate = DateTime.UtcNow,
            UserId = requestedByUserId,
            RequestedByUserId = requestedByUserId,
            WithdrawType = withdrawType,
            MarketId = marketId,
            ApprovalStatus = WithdrawalApprovalStatus.Pending,
        };
        _context.CashWithdrawals.Add(withdrawal);
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogActionAsync(
            AuditEntityTypes.CashRegister, withdrawal.Id, AuditActions.Withdraw, requestedByUserId,
            new { withdrawal.Amount, requested = true }, cancellationToken);

        // Notify the owner on Telegram (best-effort, gated by settings).
        var settings = await _settings.GetOrCreateAsync(marketId, cancellationToken);
        if (settings.NotifyWithdrawalRequests)
        {
            var who = await _context.Users.Where(u => u.Id == requestedByUserId)
                .Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken);
            await _telegram.SendToOwnerAsync(marketId,
                $"<b>Запрос на снятие наличных</b>\nСумма: {request.Amount:N0} сум\nОт: {who ?? "—"}\n{request.Comment}",
                cancellationToken);
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> ApproveWithdrawalAsync(Guid withdrawalId, Guid approverUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var result = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var w = await _context.CashWithdrawals
                .FirstOrDefaultAsync(x => x.Id == withdrawalId && x.MarketId == marketId, cancellationToken);
            if (w is null)
                return Result.Failure<bool>("Запрос не найден.", "NOT_FOUND");
            if (w.ApprovalStatus != WithdrawalApprovalStatus.Pending)
                return Result.Failure<bool>("Запрос уже обработан.");

            // Cash withdrawals debit the till at approval time (with a balance
            // check). 'click' withdrawals never touch the cash balance.
            if (w.WithdrawType == WithdrawTypeCash)
            {
                var register = await GetOrCreateRegisterAsync(marketId, cancellationToken);
                if (register.CurrentBalance < w.Amount)
                    return Result.Failure<bool>("Недостаточно средств в кассе.", "INSUFFICIENT_FUNDS");
                register.CurrentBalance -= w.Amount;
                register.LastUpdated = DateTime.UtcNow;
                register.LastWithdrawalId = w.Id;
            }

            w.ApprovalStatus = WithdrawalApprovalStatus.Approved;
            w.ApprovedByUserId = approverUserId;
            w.ApprovedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(true);
        }, cancellationToken);

        if (!result.IsFailure)
            await _auditLogService.LogActionAsync(
                AuditEntityTypes.CashRegister, withdrawalId, AuditActions.Withdraw, approverUserId,
                new { approved = true }, cancellationToken);
        return result;
    }

    public async Task<Result<bool>> RejectWithdrawalAsync(Guid withdrawalId, Guid approverUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        var w = await _context.CashWithdrawals
            .FirstOrDefaultAsync(x => x.Id == withdrawalId && x.MarketId == marketId, cancellationToken);
        if (w is null)
            return Result.Failure<bool>("Запрос не найден.", "NOT_FOUND");
        if (w.ApprovalStatus != WithdrawalApprovalStatus.Pending)
            return Result.Failure<bool>("Запрос уже обработан.");

        w.ApprovalStatus = WithdrawalApprovalStatus.Rejected;
        w.ApprovedByUserId = approverUserId;
        w.ApprovedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditLogService.LogActionAsync(
            AuditEntityTypes.CashRegister, withdrawalId, AuditActions.Withdraw, approverUserId,
            new { rejected = true }, cancellationToken);
        return Result.Success(true);
    }

    public async Task<IReadOnlyList<WithdrawalListItemDto>> GetWithdrawalsAsync(string? status, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var query = _context.CashWithdrawals.AsNoTracking().Where(x => x.MarketId == marketId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WithdrawalApprovalStatus>(status, true, out var st))
            query = query.Where(x => x.ApprovalStatus == st);

        var rows = await query
            .OrderByDescending(x => x.WithdrawalDate)
            .Take(100)
            .ToListAsync(cancellationToken);

        // Resolve requester/approver names in one lookup.
        var ids = rows.SelectMany(r => new[] { r.RequestedByUserId ?? r.UserId, r.ApprovedByUserId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var names = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        string? NameOf(Guid? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : null;

        return rows.Select(r => new WithdrawalListItemDto
        {
            Id = r.Id,
            Amount = r.Amount,
            Comment = r.Comment,
            WithdrawType = r.WithdrawType,
            ApprovalStatus = r.ApprovalStatus.ToString(),
            RequestedByName = NameOf(r.RequestedByUserId ?? r.UserId),
            RequestedAt = r.WithdrawalDate,
            ApprovedByName = NameOf(r.ApprovedByUserId),
            ApprovedAt = r.ApprovedAt,
        }).ToList();
    }

    public async Task<bool> AddCashAsync(decimal amount, Guid userId, string? comment = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (amount <= 0)
            {
                _logger.LogWarning("Invalid deposit amount: {Amount}", amount);
                return false;
            }

            var marketId = _currentMarketService.GetCurrentMarketId();

            // Mirror WithdrawCashAsync — wrap the read/modify/save in a
            // transaction so two concurrent deposits can't both read the
            // same balance, both add their amounts, and clobber each other.
            // The EnableRetryOnFailure on the DbContext also handles transient
            // failures, but only when the work is inside a transaction.
            var ok = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var cashRegister = await GetOrCreateRegisterAsync(marketId, cancellationToken);
                cashRegister.CurrentBalance += amount;
                cashRegister.LastUpdated = DateTime.UtcNow;

                // Касса jurnaliga naqd kiritish (kirim) — Внесение.
                _cashLedger.Record(marketId, amount, CashMovementType.Deposit, userId: userId, comment: comment);

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Cash added. Amount: {Amount}, MarketId: {MarketId}, By: {UserId}",
                    amount, marketId, userId);
                return true;
            }, cancellationToken);

            // Y3 — audit deposits the same way withdrawals are audited
            // (see WithdrawCashAsync). Done AFTER the transaction commits so
            // a failed audit row never rolls back a completed deposit.
            if (ok)
            {
                await _auditLogService.LogActionAsync(
                    AuditEntityTypes.CashRegister, Guid.NewGuid(), AuditActions.Deposit, userId,
                    new { amount }, cancellationToken);
            }

            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding cash");
            return false;
        }
    }

    public async Task<TodaySalesSummaryDto?> GetTodaySalesSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var marketId = _currentMarketService.GetCurrentMarketId();
            // Tashkent business day, expressed as a UTC half-open range.
            var todayLocal = _clock.TodayLocal;
            var (todayUtcStart, todayUtcEnd) = _clock.LocalDayToUtcRange(todayLocal);

            // Draft — hali yakunlanmagan savat, chek emas: uni "Чеков" soniga
            // qo'shsak, kassa oynasi ochilib yopilgani ham sotuv bo'lib ko'rinadi.
            // Cancelled — bekor qilingan sotuv: bekor qilish faqat Status'ni
            // o'zgartiradi, TotalAmount va Payments joyida qoladi, shuning uchun
            // filtrsiz ular tushumga qo'shilib ketardi. Boshqa hamma hisobot
            // (dashboard, moliyaviy, smena) ikkalasini ham chiqarib tashlaydi —
            // profit ham shunday hisoblanadi, ya'ni filtrsiz dashboard'dagi
            // marja % surat va maxrajni turli bazada bo'lardi.
            var todaySales = await _context.Sales
                .Where(s => s.CreatedAt >= todayUtcStart && s.CreatedAt < todayUtcEnd && s.MarketId == marketId
                            && s.Status != Domain.Enums.SaleStatus.Draft
                            && s.Status != Domain.Enums.SaleStatus.Cancelled)
                .Include(s => s.Payments)
                .ToListAsync(cancellationToken);

            var allPayments = todaySales.SelectMany(s => s.Payments).ToList();

            return new TodaySalesSummaryDto
            {
                TotalSales = todaySales.Count,
                TotalAmount = todaySales.Sum(s => s.TotalAmount),
                TotalPaid = todaySales.Sum(s => s.PaidAmount),
                DebtAmount = todaySales.Sum(s => s.TotalAmount > s.PaidAmount ? s.TotalAmount - s.PaidAmount : 0),
                CashPaid = allPayments
                    .Where(p => p.PaymentType == Domain.Enums.PaymentType.Cash)
                    .Sum(p => p.Amount),
                CardPaid = allPayments
                    .Where(p => p.PaymentType == Domain.Enums.PaymentType.Terminal ||
                                p.PaymentType == Domain.Enums.PaymentType.Transfer)
                    .Sum(p => p.Amount),
                ClickPaid = allPayments
                    .Where(p => p.PaymentType == Domain.Enums.PaymentType.Click)
                    .Sum(p => p.Amount),
                Date = todayLocal
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today's sales summary");
            return null;
        }
    }
}
