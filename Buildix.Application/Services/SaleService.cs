using Microsoft.EntityFrameworkCore;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Common;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

public class SaleService : ISaleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogService;
    private readonly IAppDbContext _context;
    private readonly ILogger<SaleService> _logger;
    private readonly ICurrentMarketService _currentMarketService;
    // Customer-credit application (shared with SaleItemService).
    private readonly ISaleCreditApplier _creditApplier;
    // Read-back after a write (e.g. returning the refreshed sale DTO). The query
    // concern lives in SaleQueryService; composing it here keeps mapping in one
    // place rather than duplicating the load+project logic.
    private readonly ISaleQueryService _saleQueryService;
    // Market business rules (shift-open sales, debt-only-regulars, debt limit).
    private readonly IMarketSettingsService _settings;

    private readonly IStockLedger _stockLedger;

    public SaleService(IUnitOfWork unitOfWork, IAuditLogService auditLogService, IAppDbContext context, ILogger<SaleService> logger, ICurrentMarketService currentMarketService, ISaleCreditApplier creditApplier, ISaleQueryService saleQueryService, IMarketSettingsService settings, IStockLedger stockLedger, IExternalPayoutLedger externalPayouts)
    {
        _unitOfWork = unitOfWork;
        _auditLogService = auditLogService;
        _context = context;
        _logger = logger;
        _currentMarketService = currentMarketService;
        _creditApplier = creditApplier;
        _saleQueryService = saleQueryService;
        _settings = settings;
        _stockLedger = stockLedger;
        _externalPayouts = externalPayouts;
    }

    private readonly IExternalPayoutLedger _externalPayouts;

    public async Task<Result<SaleDto>> CreateSaleAsync(CreateSaleDto request, Guid sellerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // The seller's open drawer session, if any. Used for two things: the
        // shift-open business rule below, and stamping the sale with the shift
        // it belongs to so the receipt can print "Смена №N".
        var openShiftId = await _context.Shifts
            .Where(x => x.UserId == sellerId && x.MarketId == marketId && x.ClosedAt == null)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Business rule: sales only with an open shift (Sellers only — Admin/Owner
        // may sell without one). Gated by MarketSettings.SalesOnlyWhenShiftOpen.
        var settings = await _settings.GetOrCreateAsync(marketId, cancellationToken);
        if (settings.SalesOnlyWhenShiftOpen && openShiftId is null)
        {
            var sellerRole = await _context.Users
                .Where(u => u.Id == sellerId)
                .Select(u => (Role?)u.Role)
                .FirstOrDefaultAsync(cancellationToken);
            if (sellerRole == Role.Seller)
                return Result.Failure<SaleDto>(
                    "Смена не открыта. Откройте смену перед продажей.", "SHIFT_NOT_OPEN");
        }

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            SellerId = sellerId,
            ShiftId = openShiftId,   // drawer session this receipt belongs to
            CustomerId = request.CustomerId,
            Status = SaleStatus.Draft,
            TotalAmount = 0,
            PaidAmount = 0,
            MarketId = marketId  // Multi-tenancy
        };

        // Atomic: the Sale insert, any customer-credit application, and the
        // audit row must commit together. Previously these were three separate
        // SaveChanges — a failure mid-way left a committed Sale with no credit
        // applied and no audit trail.
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Per-market sequential receipt number (ЧЕК №). Allocate INSIDE the
            // transaction behind a per-market advisory lock so two concurrent
            // creates can't grab the same number — the old bare max+1 (computed
            // before the txn) had a documented "may reuse a number" race, which
            // is a real audit problem for a financial document. Gaps from
            // abandoned drafts remain acceptable (like a physical receipt roll).
            await MarketSequenceLock.AcquireAsync(
                _context, MarketSequenceLock.SaleNumberClass, marketId, cancellationToken);
            sale.SaleNumber = (await _context.Sales
                .Where(s => s.MarketId == marketId)
                .MaxAsync(s => (int?)s.SaleNumber, cancellationToken) ?? 0) + 1;

            await _unitOfWork.Sales.AddAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // If customer is specified, apply any available credit
            if (request.CustomerId.HasValue)
            {
                await _creditApplier.ApplyAsync(sale.Id, request.CustomerId.Value, cancellationToken);
            }

            await _auditLogService.LogSaleActionAsync(sale.Id, "Create", sellerId, cancellationToken);
        }, cancellationToken);

        return Result.Success(await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken));
    }

    public async Task<Result<SaleDto>> UpdateSaleCustomerAsync(Guid saleId, UpdateSaleCustomerDto request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var sales = await _unitOfWork.Sales.FindAsync(
            s => s.Id == saleId && s.MarketId == marketId,
            cancellationToken);
        var sale = sales.FirstOrDefault();

        if (sale is null)
            return Result.Failure<SaleDto>("Sale not found", "NOT_FOUND");

        // Faqat Draft va Debt statusdagi savdolarga mijoz qo'shish mumkin
        if (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Debt)
            return Result.Failure<SaleDto>("Faqat Draft va Debt statusdagi savdolarga mijoz qo'shish mumkin");

        // Agar mijoz berilgan bo'lsa, u shu do'konga tegishli ekanligini tekshiramiz
        if (request.CustomerId.HasValue)
        {
            var customers = await _unitOfWork.Customers.FindAsync(
                c => c.Id == request.CustomerId.Value && c.MarketId == marketId,
                cancellationToken);
            var customer = customers.FirstOrDefault();

            if (customer is null)
                return Result.Failure<SaleDto>("Mijoz topilmadi yoki bu do'konga tegishli emas");
        }

        // Mijozni yangilaymiz
        sale.CustomerId = request.CustomerId;
        _unitOfWork.Sales.Update(sale);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // If customer is specified, apply any available credit
        if (request.CustomerId.HasValue)
        {
            await _creditApplier.ApplyAsync(saleId, request.CustomerId.Value, cancellationToken);
        }

        return Result.Success(await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken));
    }



    /// <summary>
    /// Recompute Sale.TotalAmount from the authoritative SUM over its SaleItems.
    /// Replaces the old `sale.TotalAmount += x` / `-= x` arithmetic that was
    /// race-prone under concurrent AddSaleItem / RemoveSaleItem calls — two
    /// callers could each read the same stale in-memory total, each apply
    /// their own delta, and the last write would clobber the other's
    /// contribution. SUM-from-DB is deterministic regardless of order.
    /// Callers should SaveChanges BEFORE invoking this so newly added/removed
    /// items are visible in the SUM.
    /// </summary>
    private async Task RecalculateSaleTotalAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        // Delegates to the shared SaleTotals helper so the charged-total formula
        // (SUM of items − discount, clamped at 0) lives in exactly one place,
        // used identically by SalePaymentService.
        await SaleTotals.RecalculateAsync(_context, sale, cancellationToken);
        _unitOfWork.Sales.Update(sale);
    }


    /// <summary>
    /// Public method for ISaleService - applies customer credit and returns updated sale
    /// </summary>
    public async Task<Result<SaleDto>> ApplyCustomerCreditAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Get sale to find customer
        var sale = await _unitOfWork.Sales.FindAsync(
            s => s.Id == saleId && s.MarketId == marketId,
            cancellationToken);
        var saleEntity = sale.FirstOrDefault();

        if (saleEntity == null)
            return Result.Failure<SaleDto>("Sale not found", "NOT_FOUND");

        if (!saleEntity.CustomerId.HasValue)
            return Result.Failure<SaleDto>("No customer to apply credit to", "NOT_FOUND");

        // Apply credit using internal method
        await _creditApplier.ApplyAsync(saleId, saleEntity.CustomerId.Value, cancellationToken);

        // Return updated sale (null => 404, mirroring the pre-Result behaviour)
        var refreshed = await _saleQueryService.GetSaleByIdAsync(saleId, cancellationToken);
        return refreshed is null
            ? Result.Failure<SaleDto>("Sale not found", "NOT_FOUND")
            : Result.Success(refreshed);
    }


    /// <summary>
    /// Marks a sale as debt status
    /// </summary>
    public async Task<Result<SaleDto>> MarkSaleAsDebtAsync(Guid saleId, Guid userId, DateTime? dueDate = null, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Npgsql timestamptz UTC talab qiladi — kun sanasini UTC deb belgilaymiz.
        if (dueDate.HasValue)
            dueDate = DateTime.SpecifyKind(dueDate.Value.Date, DateTimeKind.Utc);

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var sales = await _unitOfWork.Sales.FindAsync(
                s => s.Id == saleId && s.MarketId == marketId,
                cancellationToken);
            var sale = sales.FirstOrDefault();

            if (sale is null)
                return Result.Failure<SaleDto>("Sale not found", "NOT_FOUND");

            if (sale.PaidAmount >= sale.TotalAmount)
                return Result.Failure<SaleDto>("Sale is already fully paid, cannot mark as debt");

            // ── Mijoz SHART ──────────────────────────────────────────────────
            // Qarz — kimningdir qarzi. Mijozsiz chekda `Debt` yozuvi umuman
            // yaratilmasdi (u pastda `sale.CustomerId.HasValue` sharti bilan
            // himoyalangan): sotuv holati «Qarz» bo'lib qolar, «Qarzlar»
            // ro'yxatida esa hech qachon ko'rinmasdi. Ya'ni tovar do'kondan
            // chiqib ketar, pul kelmas va uni kimdan undirish kerakligini
            // hech narsa ko'rsatmasdi.
            //
            // To'lov yo'lida bu tekshiruv allaqachon bor
            // (SalePaymentService — «Mijoz tanlanmagan savdoni qarzga yopib
            // bo'lmaydi»), bu yerda esa tushib qolgan edi.
            if (!sale.CustomerId.HasValue || sale.CustomerId.Value == Guid.Empty)
                return Result.Failure<SaleDto>(
                    "Mijoz tanlanmagan savdoni qarzga yozib bo'lmaydi. Mijoz tanlang.");

            // ── Business rules: debt-only-for-regulars + per-customer debt limit ──
            var settings = await _settings.GetOrCreateAsync(marketId, cancellationToken);
            var saleRemaining = sale.TotalAmount - sale.PaidAmount;

            Customer? customer = null;
            if (sale.CustomerId.HasValue)
                customer = await _context.Customers
                    .FirstOrDefaultAsync(c => c.Id == sale.CustomerId.Value && c.MarketId == marketId, cancellationToken);

            if (settings.DebtOnlyForRegulars && (customer is null || !customer.IsRegular))
                return Result.Failure<SaleDto>(
                    "Долг разрешён только постоянным клиентам.", "DEBT_REGULARS_ONLY");

            // Per-user «Долг на чек» limiti: kassir bitta chekka belgilangan
            // summadan ko'p qarz qoldira olmaydi. Null = cheksiz.
            var maxDebtPerCheck = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.MaxDebtPerCheck)
                .FirstOrDefaultAsync(cancellationToken);
            if (maxDebtPerCheck is { } cap && saleRemaining > cap)
                return Result.Failure<SaleDto>($"Sizning bir chekka qarz limitingiz {cap:N0} сум.");

            // Limit: customer-specific overrides the market default; 0 = unlimited.
            if (customer is not null)
            {
                var limit = customer.DebtLimit ?? settings.DefaultDebtLimit;
                if (limit > 0)
                {
                    var currentDebt = await _context.Debts
                        .Where(d => d.CustomerId == customer.Id && d.MarketId == marketId && d.Status == DebtStatus.Open)
                        .SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;
                    if (currentDebt + saleRemaining > limit)
                        return Result.Failure<SaleDto>(
                            $"Превышен лимит долга клиента ({limit:N0} сум).", "DEBT_LIMIT_EXCEEDED");
                }
            }

            // Draft'dan qarzga o'tish — sotuv shu yerda yakunlanadi; ombor
            // jurnaliga Продажа harakati (delta = −miqdor) yoziladi. Faqat
            // Draft'dan (statusni o'zgartirishdan oldin tekshiramiz).
            var startedAsDraft = sale.Status == SaleStatus.Draft;

            sale.Status = SaleStatus.Debt;
            _unitOfWork.Sales.Update(sale);

            if (startedAsDraft)
            {
                await _stockLedger.RecordSaleFinalizationAsync(sale, cancellationToken);
                // Qo'shni do'kondan olingan tovar puli kassadan chiqadi — mijoz
                // qarzga olgan bo'lsa ham. To'lov yo'lidagi bilan bir xil shart.
                await _externalPayouts.RecordAsync(sale, cancellationToken);
            }

            // Create or update debt record
            var existingDebt = await _unitOfWork.Debts.FindAsync(
                d => d.SaleId == saleId && d.MarketId == marketId,
                cancellationToken);
            var debt = existingDebt.FirstOrDefault();

            if (debt == null && sale.CustomerId.HasValue)
            {
                debt = new Debt
                {
                    Id = Guid.NewGuid(),
                    SaleId = sale.Id,
                    CustomerId = sale.CustomerId.Value,
                    MarketId = marketId,
                    TotalDebt = sale.TotalAmount,
                    RemainingDebt = sale.TotalAmount - sale.PaidAmount,
                    Status = DebtStatus.Open,
                    DueDate = dueDate,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.Debts.AddAsync(debt, cancellationToken);
            }

            // Fraud audit: converting a sale to debt defers real money — record
            // who authorised it, for which customer, and the amount left owing.
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, saleId, AuditActions.MarkDebt, userId,
                new
                {
                    SaleId = saleId,
                    sale.CustomerId,
                    RemainingDebt = saleRemaining,
                    DueDate = dueDate,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken));
        }, cancellationToken);
    }


    /// <summary>
    /// Sets a sale-level chegirma (skidka) on a Draft/Debt sale. Mirrors
    /// UpdateSaleItemPriceAsync: persist the discount, recompute the charged
    /// TotalAmount via the single choke point (RecalculateSaleTotalAsync now
    /// subtracts DiscountAmount), then re-sync any open debt against the new
    /// total. Item SalePrices are left untouched, so per-item history and the
    /// invoice line items stay intact — only the bill total drops.
    /// </summary>
    public async Task<Result<SaleDto>> SetSaleDiscountAsync(Guid saleId, decimal discountAmount, Guid userId, CancellationToken cancellationToken = default)
    {
        if (discountAmount < 0)
            return Result.Failure<SaleDto>("Chegirma manfiy bo'lmasin");

        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var sale = await _context.Sales
                .FirstOrDefaultAsync(s => s.Id == saleId && s.MarketId == marketId, cancellationToken);

            if (sale == null)
                return Result.Failure<SaleDto>("Sotuv topilmadi");

            // Chegirmani faqat qurilayotgan (Draft) yoki Qarz holatidagi
            // sotuvda o'zgartirish mumkin — yakunlangan (Paid/Closed/Cancelled)
            // sotuvning tarixiy summasi muzlatilgan bo'lishi kerak.
            if (sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Debt)
                return Result.Failure<SaleDto>(
                    "Chegirmani faqat Draft yoki Qarz holatidagi sotuvlarda qo'llash mumkin");

            // Chegirma jami item summasidan oshmasin — aks holda TotalAmount 0 ga
            // clamp bo'lib, oshib ketgan qism jimgina yo'qolardi.
            var gross = await _context.SaleItems
                .Where(si => si.SaleId == sale.Id)
                .SumAsync(si => si.SalePrice * si.Quantity, cancellationToken);
            if (discountAmount > gross)
                return Result.Failure<SaleDto>("Chegirma jami summadan oshib ketmasligi kerak");

            // ── Chegirma TO'LANGAN summadan pastga tushira olmaydi ───────────
            // Chegirma qarzdagi chekka ham qo'yiladi (bu ataylab: mijoz bilan
            // kelishilgan chegirma ko'pincha keyin paydo bo'ladi). Lekin unda
            // allaqachon to'lov bo'lgan bo'lishi mumkin va yangi jami o'sha
            // summadan past bo'lsa, chek «ortiqcha to'langan» holatga tushadi.
            //
            // Bu holat QAYTMAS edi: undan keyin har qanday to'lov urinishi
            // «Bu savdo allaqachon to'liq to'langan» degan tushunarsiz xabarga
            // urilardi — kassir esa yangi chek ochyapman deb o'ylardi. Chek
            // shu holatda abadiy qotib qolardi.
            //
            // Ortiqchani qaytarish alohida amal (qaytarish/kassa chiqimi),
            // shuning uchun bu yerda chegirma shunchaki rad etiladi va kassir
            // qancha qo'ya olishini ko'radi.
            if (gross - discountAmount < sale.PaidAmount)
            {
                var maxDiscount = gross - sale.PaidAmount;
                return Result.Failure<SaleDto>(
                    $"Chek bo'yicha {MoneyText.Sum(sale.PaidAmount)} so'm allaqachon to'langan — " +
                    $"chegirma {MoneyText.Sum(maxDiscount)} so'mdan oshmasligi kerak.");
            }

            // Per-user chegirma limiti (%): kassir belgilangan foizdan ko'p
            // chegirma qo'ya olmaydi. Null = cheksiz (Owner/Admin va limitsizlar).
            var maxDiscountPct = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.MaxDiscountPercent)
                .FirstOrDefaultAsync(cancellationToken);
            if (maxDiscountPct is { } pct && gross > 0)
            {
                var maxAllowed = gross * pct / 100m;
                if (discountAmount > maxAllowed)
                    return Result.Failure<SaleDto>($"Sizning chegirma limitingiz {pct}% ({maxAllowed:N0} сум).");
            }

            var oldDiscount = sale.DiscountAmount;
            sale.DiscountAmount = discountAmount;
            _unitOfWork.Sales.Update(sale);

            // Fraud audit: a discount (esp. on a Debt sale) reduces what the
            // customer owes — record the actor + old→new discount.
            await _auditLogService.EnqueueActionAsync(
                AuditEntityTypes.Sale, saleId, AuditActions.Discount, userId,
                new
                {
                    SaleId = saleId,
                    OldDiscount = oldDiscount,
                    NewDiscount = discountAmount,
                    Status = sale.Status.ToString(),
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Recompute charged total (gross − discount) via the choke point.
            await RecalculateSaleTotalAsync(sale, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (sale.Status == SaleStatus.Debt)
            {
                var debt = await _context.Debts
                    .FirstOrDefaultAsync(d => d.SaleId == sale.Id && d.MarketId == marketId, cancellationToken);
                if (debt != null)
                {
                    debt.TotalDebt = sale.TotalAmount;
                    debt.RemainingDebt = Math.Max(0, sale.TotalAmount - sale.PaidAmount);
                    if (debt.RemainingDebt <= 0)
                        debt.Status = DebtStatus.Closed;
                    _context.Debts.Update(debt);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            return Result.Success(await SaleMapper.MapToDtoAsync(sale, _unitOfWork, cancellationToken));
        }, cancellationToken);
    }
}
