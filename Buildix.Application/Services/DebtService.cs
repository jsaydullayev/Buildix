using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

public class DebtService : IDebtService
{
    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentMarketService _currentMarket;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<DebtService> _logger;

    public DebtService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        ICurrentMarketService currentMarket,
        IAuditLogService auditLog,
        ILogger<DebtService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _currentMarket = currentMarket;
        _auditLog = auditLog;
        _logger = logger;
    }

    public async Task<Result<PayDebtResultDto>> PayAsync(Guid debtId, PayDebtDto request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Result.Failure<PayDebtResultDto>("To'lov miqdori 0 dan katta bo'lishi kerak.");

        var marketId = _currentMarket.GetCurrentMarketId();

        // Capture the payment id for the post-commit, fire-and-forget audit log.
        Guid paymentId = Guid.Empty;

        // The debt-payment path was the last place hand-rolling its own
        // CreateExecutionStrategy() + BeginTransactionAsync. It now shares
        // UnitOfWork.ExecuteInTransactionAsync with every other transactional
        // write: one commit/rollback path plus the 3× optimistic-concurrency
        // retry (Debt/Sale carry xmin tokens). The SELECT … FOR UPDATE locks
        // below still run inside that managed transaction.
        var payResult = await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Y6 — SELECT … FOR UPDATE blocks a parallel /api/Debts/{id}/pay
            // request until we commit. Without this, two concurrent payments
            // would both see RemainingDebt = 100, both subtract, both add
            // their full amount into the cash register — the customer ends
            // up paying twice for the same debt and the till is over.
            //
            // FOR UPDATE is PostgreSQL-only syntax. The integration test
            // suite uses EF Core InMemory which doesn't understand raw SQL,
            // so on the InMemory provider we fall back to a plain
            // FirstOrDefaultAsync. The Debt + Sale entities both carry an
            // Xmin concurrency token, so concurrent writes still get caught
            // there — FOR UPDATE just upgrades the conflict from "second
            // write fails and retries" to "second read blocks". Production
            // gets the stronger guarantee; tests still exercise the path.
            var isPostgres = _context.Database.ProviderName?.Contains("InMemory") == false;

            Debt? debt;
            if (isPostgres)
            {
                // NOTE: must be "SELECT *, xmin" — the Debt entity maps an
                // Xmin concurrency token to PostgreSQL's system column "xmin"
                // (AppDbContext: b.Property(x => x.Xmin).HasColumnName("xmin")
                // .IsConcurrencyToken()). PostgreSQL's `*` never expands system
                // columns, so EF Core — which wraps this FromSql in a subquery
                // and projects t.xmin — would reference a non-existent column and
                // raise 42703 (undefined_column) → PostgresException → HTTP 503.
                // The sibling Sales query (below) and the Products query in
                // SaleService both already list xmin explicitly for this reason.
                debt = await _context.Debts
                    .FromSqlInterpolated($"SELECT *, xmin FROM \"Debts\" WHERE \"Id\" = {debtId} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                debt = await _context.Debts.FirstOrDefaultAsync(d => d.Id == debtId, cancellationToken);
            }
            if (debt is null)
                return Result.Failure<PayDebtResultDto>("Qarz topilmadi.", "NOT_FOUND");

            if (debt.MarketId != marketId)
                return Result.Failure<PayDebtResultDto>("Qarz topilmadi.", "NOT_FOUND");
            if (debt.Status != DebtStatus.Open)
                return Result.Failure<PayDebtResultDto>("Bu qarz allaqachon yopilgan.");

            // The sale row needs to move with the debt so we lock that too.
            Sale? sale;
            if (isPostgres)
            {
                sale = await _context.Sales
                    .FromSqlInterpolated($"SELECT *, xmin FROM \"Sales\" WHERE \"Id\" = {debt.SaleId} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                sale = await _context.Sales.FirstOrDefaultAsync(s => s.Id == debt.SaleId, cancellationToken);
            }
            if (sale is null)
                return Result.Failure<PayDebtResultDto>("Savdo topilmadi.", "NOT_FOUND");
            if (sale.MarketId != marketId)
                return Result.Failure<PayDebtResultDto>("Savdo topilmadi.", "NOT_FOUND");

            if (request.Amount > debt.RemainingDebt)
                return Result.Failure<PayDebtResultDto>(
                    $"To'lov miqdori ({request.Amount}) qoldiq qarzdan ({debt.RemainingDebt}) katta.");

            // Map client's "CARD" alias to the canonical Terminal enum.
            var paymentTypeStr = string.Equals(request.PaymentType, "CARD", StringComparison.OrdinalIgnoreCase)
                ? "Terminal" : request.PaymentType;

            if (!Enum.TryParse<PaymentType>(paymentTypeStr, ignoreCase: true, out var paymentType))
                return Result.Failure<PayDebtResultDto>($"Noto'g'ri to'lov turi: {request.PaymentType}");

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                SaleId = debt.SaleId,
                PaymentType = paymentType,
                Amount = request.Amount,
                MarketId = marketId,
                // The money lands in THIS cashier's drawer, not the original
                // seller's — record it so shift reconciliation credits the right
                // person (otherwise B collects and A's closed shift "owns" it).
                CollectedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.Payments.Add(payment);

            if (paymentType == PaymentType.Cash)
            {
                var register = await _context.CashRegisters
                    .FirstOrDefaultAsync(cr => cr.MarketId == marketId, cancellationToken);
                if (register == null)
                {
                    // Defence in depth: AuthService and the migration both seed
                    // a register per market, but if someone deleted it manually
                    // we recreate rather than 500.
                    register = new CashRegister
                    {
                        Id = Guid.NewGuid(),
                        MarketId = marketId,
                        CurrentBalance = 0m,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.CashRegisters.Add(register);
                }
                register.CurrentBalance += request.Amount;
                register.LastUpdated = DateTime.UtcNow;
            }

            debt.RemainingDebt -= request.Amount;
            if (debt.RemainingDebt <= 0)
            {
                debt.RemainingDebt = 0;
                debt.Status = DebtStatus.Closed;
                sale.Status = SaleStatus.Closed;
            }
            sale.PaidAmount += request.Amount;

            await _context.SaveChangesAsync(cancellationToken);
            paymentId = payment.Id;

            return Result.Success(new PayDebtResultDto(
                debt.RemainingDebt,
                request.Amount,
                debt.Status.ToString()));
        }, cancellationToken);

        // Post-commit, fire-and-forget audit + log — outside the transaction so
        // a logging hiccup can never roll back a committed payment.
        if (payResult.IsSuccess)
        {
            await _auditLog.LogPaymentActionAsync(paymentId, actorUserId, cancellationToken);
            _logger.LogInformation(
                "Debt payment recorded: DebtId={DebtId} Amount={Amount} Remaining={Remaining} ByUser={UserId}",
                debtId, request.Amount, payResult.Value.RemainingDebt, actorUserId);
        }

        return payResult;
    }

    public async Task<Result<DebtDto>> UpdateDueDateAsync(Guid debtId, DateTime? dueDate, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarket.GetCurrentMarketId();

        var debt = await _context.Debts
            .Include(d => d.Customer)
            .Include(d => d.Sale)
                .ThenInclude(s => s!.SaleItems)
                    .ThenInclude(si => si.Product)
            .FirstOrDefaultAsync(d => d.Id == debtId && d.MarketId == marketId, cancellationToken);

        if (debt is null)
            return Result.Failure<DebtDto>("Qarz topilmadi.", "NOT_FOUND");

        // Npgsql timestamptz faqat UTC qabul qiladi; kelgan sanani (faqat kun,
        // vaqtsiz) UTC deb belgilaymiz — aks holda "Kind=Unspecified" 500 beradi.
        debt.DueDate = dueDate.HasValue
            ? DateTime.SpecifyKind(dueDate.Value.Date, DateTimeKind.Utc)
            : null;
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Debt {DebtId} due date updated to {DueDate}.", debtId, dueDate);
        return Result.Success(DebtMapper.MapToDto(debt));
    }

}
