using Microsoft.EntityFrameworkCore;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Common;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Payment concern extracted from SaleService. Records a payment against a sale
/// and drives the resulting status (Paid / Closed / Debt), debt record, and
/// cash-register balance. See <see cref="ISalePaymentService"/>.
/// </summary>
public class SalePaymentService : ISalePaymentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SalePaymentService> _logger;
    private readonly IMarketSettingsService _settings;
    private readonly IStockLedger _stockLedger;
    private readonly ICashLedger _cashLedger;
    private readonly ISaleCreditApplier _creditApplier;

    public SalePaymentService(
        IUnitOfWork unitOfWork,
        IAppDbContext context,
        ICurrentMarketService currentMarketService,
        IAuditLogService auditLogService,
        ILogger<SalePaymentService> logger,
        IMarketSettingsService settings,
        IStockLedger stockLedger,
        ICashLedger cashLedger,
        ISaleCreditApplier creditApplier,
        IExternalPayoutLedger externalPayouts)
    {
        _creditApplier = creditApplier;
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _auditLogService = auditLogService;
        _logger = logger;
        _settings = settings;
        _stockLedger = stockLedger;
        _cashLedger = cashLedger;
        _externalPayouts = externalPayouts;
    }

    private readonly IExternalPayoutLedger _externalPayouts;

    /// <summary>
    /// Enforces MarketSettings.DebtOnlyForRegulars + the per-customer/market debt
    /// limit when a partial payment is about to convert a sale into a debt. This
    /// mirrors SaleService.MarkSaleAsDebtAsync so the "В долг" rules cannot be
    /// bypassed via the partial-payment path (review H-1). Returns a failed
    /// Result on violation (the value is never read); an ok Result otherwise.
    /// </summary>
    private async Task<Result<PaymentDto>> CheckDebtRulesAsync(
        Guid customerId, int marketId, Guid saleId, Guid sellerId,
        decimal newRemainingDebt, CancellationToken ct)
    {
        var settings = await _settings.GetOrCreateAsync(marketId, ct);
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId && c.MarketId == marketId, ct);

        if (settings.DebtOnlyForRegulars && (customer is null || !customer.IsRegular))
            return Result.Failure<PaymentDto>("Долг разрешён только постоянным клиентам.", "DEBT_REGULARS_ONLY");

        // Kassirning bitta chekka qarz limiti. Ilgari u FAQAT «Qarzga»
        // tugmasida tekshirilardi, ya'ni limitdan oshgan kassir shunchaki
        // 1 so'mlik to'lov qabul qilib o'sha qarzni yozaverardi — qoida
        // bitta tugmani chetlab o'tish bilan yo'qolardi. Null = cheksiz.
        var maxDebtPerCheck = await _context.Users
            .Where(u => u.Id == sellerId)
            .Select(u => u.MaxDebtPerCheck)
            .FirstOrDefaultAsync(ct);
        if (maxDebtPerCheck is { } cap && newRemainingDebt > cap)
            return Result.Failure<PaymentDto>($"Sizning bir chekka qarz limitingiz {cap:N0} сум.");

        if (customer is not null)
        {
            var limit = customer.DebtLimit ?? settings.DefaultDebtLimit;
            if (limit > 0)
            {
                // Other open debts for this customer (exclude this sale's own row).
                var otherDebt = await _context.Debts
                    .Where(d => d.CustomerId == customerId && d.MarketId == marketId
                        && d.Status == DebtStatus.Open && d.SaleId != saleId)
                    .SumAsync(d => (decimal?)d.RemainingDebt, ct) ?? 0m;
                if (otherDebt + newRemainingDebt > limit)
                    return Result.Failure<PaymentDto>(
                        $"Превышен лимит долга клиента ({limit:N0} сум).", "DEBT_LIMIT_EXCEEDED");
            }
        }
        return Result.Success<PaymentDto>(null!); // ok — value never read
    }

    /// <summary>One tender being applied to a sale.</summary>
    private sealed record Tender(PaymentType Type, decimal Amount);

    /// <summary>Validate + normalise the client's tenders ("CARD" ⇒ Terminal).</summary>
    private static (List<Tender>? Tenders, string? Error) ParseTenders(
        IReadOnlyList<(string Type, decimal Amount)> raw)
    {
        if (raw.Count == 0) return (null, "To'lov turlari ko'rsatilmagan.");

        var tenders = new List<Tender>(raw.Count);
        foreach (var (typeStr, amount) in raw)
        {
            if (amount <= 0) return (null, "Payment amount must be greater than 0");
            var normalised = string.Equals(typeStr, "CARD", StringComparison.OrdinalIgnoreCase)
                ? "Terminal"
                : typeStr;
            if (!Enum.TryParse<PaymentType>(normalised, ignoreCase: true, out var type))
                return (null, $"Noto'g'ri to'lov turi: {typeStr}");
            tenders.Add(new Tender(type, amount));
        }
        return (tenders, null);
    }

    public async Task<Result<PaymentDto>> AddPaymentAsync(Guid saleId, AddPaymentDto request, CancellationToken cancellationToken = default)
    {
        // Nol summa — «to'lanadigan narsa yo'q, chekni yop» degani. Bu butun
        // summasi chegirmaga ketgan chek uchun: kassa jamini yuboradi, u esa
        // nol. Ilgari bunday chaqiruv «summa noldan katta bo'lishi kerak» deb
        // rad etilardi va chek yopilmay qolardi — kassir uni yopishning
        // hech qanday yo'lini topa olmasdi.
        //
        // Haqiqiy to'lov ekanini ApplyTendersAsync hal qiladi: qoldiq bo'lsa
        // nol to'lov chekni yopmaydi, u yerda qoldiq tekshiriladi.
        if (request.Amount == 0)
            return await ApplyTendersAsync(saleId, [], request.DueDate, cancellationToken);

        var (tenders, error) = ParseTenders(new[] { (request.PaymentType, request.Amount) });
        if (error is not null) return Result.Failure<PaymentDto>(error);
        return await ApplyTendersAsync(saleId, tenders!, request.DueDate, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PaymentDto>> CheckoutAsync(Guid saleId, CheckoutSaleDto request, CancellationToken cancellationToken = default)
    {
        var (tenders, error) = ParseTenders(
            (request.Tenders ?? Array.Empty<CheckoutTenderDto>()).Select(x => (x.PaymentType, x.Amount)).ToList());
        if (error is not null) return Result.Failure<PaymentDto>(error);
        return await ApplyTendersAsync(saleId, tenders!, request.DueDate, cancellationToken);
    }

    /// <summary>
    /// The single money path behind both AddPayment and Checkout. Every tender is
    /// applied inside ONE transaction and the resulting PaidAmount drives status +
    /// debt exactly once — so a split that covers the bill never passes through an
    /// intermediate "partial ⇒ debt" state, and the no-customer guard sees the
    /// full tendered amount rather than the first instalment.
    /// </summary>
    private async Task<Result<PaymentDto>> ApplyTendersAsync(
        Guid saleId, IReadOnlyList<Tender> tenders, DateTime? dueDate, CancellationToken cancellationToken)
    {
        var totalTendered = tenders.Sum(x => x.Amount);
        var marketId = _currentMarketService.GetCurrentMarketId();

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // S-race — lock the Sale row FOR UPDATE before the read-modify-write
            // below. AddPaymentAsync does `sale.PaidAmount += amount` and then
            // derives Status + the Debt record from that value. Sale carries no
            // concurrency token (disabled by design), so without this lock two
            // concurrent payments on the same sale would both read the same
            // PaidAmount, both add their amount, and the last write would clobber
            // the other — a lost payment and a possibly-wrong terminal status /
            // debt balance. DebtService.PayAsync already locks the Sale the same
            // way. FOR UPDATE is PostgreSQL-only; the InMemory test provider skips
            // it. Must be "SELECT *, xmin": Sale maps xmin to PostgreSQL's system
            // column and PG's `*` never expands system columns, so omitting it
            // raises 42703 (undefined_column). The lock query loads + tracks the
            // row, and GetWithItemsAsync below returns that same tracked instance
            // (with SaleItems populated) rather than issuing a second read.
            if (_context.Database.ProviderName?.Contains("InMemory") == false)
            {
                await _context.Sales
                    .FromSqlInterpolated($"SELECT *, xmin FROM \"Sales\" WHERE \"Id\" = {saleId} FOR UPDATE")
                    .FirstOrDefaultAsync(cancellationToken);
            }

            // Repository now enforces MarketId at the query layer — a sale in
            // another tenant returns null here, same as a non-existent id.
            var sale = await _unitOfWork.Sales.GetWithItemsAsync(saleId, marketId, cancellationToken);

            if (sale is null)
                return Result.Failure<PaymentDto>("Sale not found");

            if (sale.Status == SaleStatus.Paid || sale.Status == SaleStatus.Closed || sale.Status == SaleStatus.Cancelled)
                return Result.Failure<PaymentDto>($"Cannot add payment to sale with status: {sale.Status}");

            // Ombor jurnaliga sotuv harakati faqat Draft'dan chiqishda yoziladi
            // (qarzni keyin to'lash stokni harakatlantirmaydi — u allaqachon qayd
            // etilgan). Statusni mutatsiyadan OLDIN ushlaymiz.
            var startedAsDraft = sale.Status == SaleStatus.Draft;

            // Log payment details with structured properties
            _logger.LogInformation("Applying {TenderCount} tender(s) totalling {PaymentAmount} to sale {SaleId}, " +
                "TotalAmount: {TotalAmount}, PaidAmount: {PaidAmount}, Status: {Status}, ItemsCount: {ItemsCount}",
                tenders.Count, totalTendered, sale.Id, sale.TotalAmount, sale.PaidAmount, sale.Status, sale.SaleItems?.Count ?? 0);

            if (sale.SaleItems != null)
            {
                foreach (var item in sale.SaleItems)
                {
                    decimal itemTotal = item.SalePrice * item.Quantity;
                    _logger.LogDebug("Sale item: ProductId={ProductId}, Quantity={Quantity}, SalePrice={SalePrice}, Total={Total}",
                        item.ProductId, item.Quantity, item.SalePrice, itemTotal);
                }
            }

            // Authoritative total from a server-side SUM of the sale's items.
            // This used to be recomputed from the in-memory SaleItems collection
            // further down — which is stale if items were added/removed by a
            // concurrent request. Computing it here (once) makes every check
            // below (already-paid, over-payment, debt) use the real total.
            await SaleTotals.RecalculateAsync(_context, sale, cancellationToken);

            // Ortiqcha AVANSNI qaytarish — qoldiq hisoblanishidan OLDIN.
            //
            // Chek kichrayganda avans qo'llaydigan yo'llarning o'zi uni
            // qaytaradi, lekin eski cheklarda ortiqcha qolib ketgan bo'lishi
            // mumkin. Bu yerda qaytarilmasa, quyidagi `owed` manfiy chiqar va
            // chek nol to'lov bilan JIMGINA yopilardi: mijozning avansi
            // chekda qolib ketar, keyingi bekor qilish esa uni ikkinchi marta
            // to'lardi. Ortiqcha bo'lmasa chaqiruv bitta o'qishdan keyin
            // qaytadi va hech narsa yozmaydi.
            await _creditApplier.ReleaseAsync(saleId, cancellationToken);

            // ── To'lanadigan qoldiq ─────────────────────────────────────────
            // Quyidagi ikkala himoya ham ilgari `TotalAmount > 0` shartiga
            // bog'langan edi va jami NOLGA tushganda (butun summa chegirmaga
            // ketsa) ikkalasi ham chetlab o'tilardi. Oqibati og'ir edi: bunday
            // chek cheksiz pul qabul qilaverardi, qoldiq manfiy bo'lib ketardi
            // va chek Draft holatida abadiy ochiq qolardi. Keyin unga tovar
            // qo'shilsa, to'langan summa yangi jamidan katta bo'lib qolar va
            // kassir «Bu savdo allaqachon to'liq to'langan» degan tushunarsiz
            // xabarga urilardi — aslida muammo bir necha qadam oldin tug'ilgan
            // edi.
            //
            // Endi qoldiq bitta joyda hisoblanadi va himoyalar jamining
            // qiymatidan QAT'I NAZAR ishlaydi.
            var owed = sale.TotalAmount - sale.PaidAmount;

            if (totalTendered > 0 && owed <= 0)
            {
                if (sale.TotalAmount <= 0)
                    return Result.Failure<PaymentDto>("Bu chek bo'yicha to'lanadigan summa yo'q.");

                // Ortiqcha to'langan chek ALOHIDA aytiladi. Ilgari u ham
                // «allaqachon to'liq to'langan» degan bir xil xabarga tushardi
                // va kassir uni yangi chek deb o'ylardi: ekranda to'lanishi
                // kerak bo'lgan summa turardi, tugma esa har safar shu xabarni
                // qaytarardi. Sabab boshqa joyda — chegirma jamini to'langan
                // summadan pastga tushirgan — va uni bu xabardan bilib
                // bo'lmasdi.
                return Result.Failure<PaymentDto>(owed < 0
                    ? $"Chek ortiqcha to'langan: {MoneyText.Sum(sale.PaidAmount)} so'm to'langan, " +
                      $"jami esa {MoneyText.Sum(sale.TotalAmount)} so'm. Chekni to'lovsiz yoping " +
                      $"va {MoneyText.Sum(-owed)} so'mni qaytaring."
                    : "Bu savdo allaqachon to'liq to'langan.");
            }

            // Ortiqcha to'lov hech qachon qabul qilinmaydi: aks holda PaidAmount
            // jamidan oshib ketardi va ortiqcha pul keyinroq mijozning soxta
            // krediti bo'lib qayta paydo bo'lardi.
            //
            // Solishtirish MANFIY BO'LMAGAN qoldiq bilan. Chek ortiqcha
            // to'langan bo'lsa (chegirma jamini to'langan summadan pastga
            // tushirgan) `owed` manfiy bo'ladi va nol to'lov ham «qoldiqdan
            // oshdi» deb rad etilardi. Ya'ni yuqoridagi xabar «chekni
            // to'lovsiz yoping» deb maslahat berar, keyingi qatorning o'zi
            // esa aynan shu yo'lni to'sib turardi — chekni yopishning hech
            // qanday usuli qolmasdi.
            if (totalTendered > Math.Max(0m, owed))
                return Result.Failure<PaymentDto>("To'lov summasi qoldiq summadan oshib ketdi.");

            // Nol to'lov FAQAT to'lanadigan narsa qolmaganda o'rinli. Qoldiq
            // bor bo'lsa, chekni pulsiz yopib bo'lmaydi — aks holda tovar
            // do'kondan pulsiz chiqib ketardi.
            if (totalTendered == 0 && owed > 0)
                return Result.Failure<PaymentDto>("To'lov summasi ko'rsatilmagan.");

            // Bo'sh chekni yopib bo'lmaydi. Nol to'lov to'liq chegirma
            // qo'yilgan HAQIQIY chek uchun; tovarsiz chek esa shunchaki
            // ochilib qolgan qoralama.
            if (totalTendered == 0 && (sale.SaleItems is null || sale.SaleItems.Count == 0))
                return Result.Failure<PaymentDto>("Bo'sh chekni yopib bo'lmaydi.");

            // VALIDATION: Mijozsiz qarzga savdo taqiqlanadi
            var newPaidAmount = sale.PaidAmount + totalTendered;
            if (newPaidAmount < sale.TotalAmount && (!sale.CustomerId.HasValue || sale.CustomerId.Value == Guid.Empty))
            {
                return Result.Failure<PaymentDto>("Mijoz tanlanmagan savdoni qarzga yopib bo'lmaydi. Iltimos, mijoz tanlang yoki to'liq to'lov qiling.");
            }

            // H-1: this partial payment will leave a debt for a real customer —
            // enforce the same debt-only-regulars + limit rules as MarkSaleAsDebt.
            // Runs BEFORE any payment/balance mutation so a violation rolls back clean.
            if (newPaidAmount < sale.TotalAmount && sale.CustomerId.HasValue && sale.CustomerId.Value != Guid.Empty)
            {
                var debtCheck = await CheckDebtRulesAsync(
                    sale.CustomerId.Value, sale.MarketId, saleId, sale.SellerId,
                    sale.TotalAmount - newPaidAmount, cancellationToken);
                if (debtCheck.IsFailure) return debtCheck;
            }

            // One Payment row per tender, so a split stays visible in the sale's
            // payment history instead of collapsing into a single line.
            var payments = new List<Payment>(tenders.Count);
            foreach (var tender in tenders)
            {
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    SaleId = saleId,
                    PaymentType = tender.Type,
                    Amount = tender.Amount,
                    MarketId = sale.MarketId  // Multi-tenancy - inherit from Sale
                };
                await _unitOfWork.Payments.AddAsync(payment, cancellationToken);
                payments.Add(payment);
            }

            // Only the cash portion moves the drawer — card / transfer / click
            // settle on external rails and never touch the register.
            var cashPortion = tenders.Where(x => x.Type == PaymentType.Cash).Sum(x => x.Amount);
            if (cashPortion > 0)
            {
                var cashRegister = await _context.CashRegisters
                    .FirstOrDefaultAsync(cr => cr.MarketId == sale.MarketId, cancellationToken);

                if (cashRegister == null)
                {
                    cashRegister = new CashRegister
                    {
                        Id = Guid.NewGuid(),
                        MarketId = sale.MarketId,
                        CurrentBalance = 0
                    };
                    _context.CashRegisters.Add(cashRegister);
                }

                cashRegister.CurrentBalance += cashPortion;

                // Касса jurnaliga sotuvning naqd ulushi (kirim) — Продажа · Ч-####.
                // Balansni bu emas, yuqoridagi CurrentBalance belgilaydi; bu faqat
                // ro'yxat. Kim: sotuvchi; qaysi smena: sotuvniki.
                _cashLedger.Record(sale.MarketId, cashPortion, CashMovementType.Sale,
                    userId: sale.SellerId, shiftId: sale.ShiftId, refNumber: sale.SaleNumber);
            }

            // Update sale paid amount. TotalAmount is already authoritative
            // (SaleTotals.RecalculateAsync ran above), so no in-memory recompute here.
            sale.PaidAmount += totalTendered;

            _logger.LogDebug("Final values for sale {SaleId} - TotalAmount: {TotalAmount}, PaidAmount: {PaidAmount}",
                sale.Id, sale.TotalAmount, sale.PaidAmount);

            // Determine new status
            _logger.LogDebug("Determining new status for sale {SaleId}: " +
                "TotalAmount={TotalAmount} (>0: {IsGreaterThan0}), " +
                "PaidAmount={PaidAmount} (>=Total: {IsPaidInFull}, >0: {IsPaidPartial}, <Total: {IsPartialPayment})",
                sale.Id, sale.TotalAmount, sale.TotalAmount > 0,
                sale.PaidAmount, sale.PaidAmount >= sale.TotalAmount,
                sale.PaidAmount > 0, sale.PaidAmount < sale.TotalAmount);

            // 1. To'liq to'langan savdo.
            //
            // `TotalAmount > 0` sharti ATAYLAB olib tashlandi: butun summa
            // chegirmaga ketgan chek ham yopilishi kerak. Ilgari u quyidagi
            // «jami nol — Draft holicha qoldiramiz» shoxiga tushib, ochiq
            // qolaverardi.
            if (sale.PaidAmount >= sale.TotalAmount)
            {
                // Semantic distinction (mirrors DebtService.PayAsync):
                //   Paid   = sale was paid in full at sale time, never had debt.
                //   Closed = sale was previously on debt (partial payment + carried),
                //            and the customer has now finished paying it off.
                // Without this branch, paying the final installment via AddPaymentAsync
                // would land on Paid while paying it via DebtService.PayAsync would land
                // on Closed — same business event, two different terminal states.
                var wasOnDebt = sale.Status == SaleStatus.Debt;
                sale.Status = wasOnDebt ? SaleStatus.Closed : SaleStatus.Paid;
                _logger.LogInformation(
                    "Sale {SaleId} is fully paid, setting status to {Status} (wasOnDebt={WasOnDebt})",
                    saleId, sale.Status, wasOnDebt);

                // Close any associated debt (filtered by market)
                var existingDebtToClose = (await _unitOfWork.Debts.FindAsync(
                    d => d.SaleId == saleId && d.MarketId == sale.MarketId,
                    cancellationToken)).FirstOrDefault();

                if (existingDebtToClose != null)
                {
                    existingDebtToClose.Status = DebtStatus.Closed;
                    existingDebtToClose.RemainingDebt = 0;
                    _unitOfWork.Debts.Update(existingDebtToClose);
                }
            }
            // 2. Qisman to'langan savdo (qarzga yopilgan)
            else if (sale.TotalAmount > 0 && sale.PaidAmount > 0 && sale.PaidAmount < sale.TotalAmount)
            {
                _logger.LogInformation("Sale {SaleId} has partial payment, setting status to Debt", saleId);
                sale.Status = SaleStatus.Debt;

                // Create or update debt record - ONLY if there's a customer
                // Mijozsiz qarzga savdo ham mumkin, status "debt" bo'ladi, lekin debt record yaratilmaydi
                if (sale.CustomerId.HasValue && sale.CustomerId.Value != Guid.Empty)
                {
                    var existingDebt = (await _unitOfWork.Debts.FindAsync(
                        d => d.SaleId == saleId && d.MarketId == sale.MarketId,
                        cancellationToken)).FirstOrDefault();

                    if (existingDebt == null)
                    {
                        var newDebt = new Debt
                        {
                            Id = Guid.NewGuid(),
                            SaleId = saleId,
                            CustomerId = sale.CustomerId.Value,
                            TotalDebt = sale.TotalAmount,
                            RemainingDebt = sale.TotalAmount - sale.PaidAmount,
                            Status = DebtStatus.Open,
                            DueDate = dueDate.HasValue
                                ? DateTime.SpecifyKind(dueDate.Value.Date, DateTimeKind.Utc)
                                : (DateTime?)null,
                            MarketId = sale.MarketId
                        };
                        await _unitOfWork.Debts.AddAsync(newDebt, cancellationToken);
                    }
                    else
                    {
                        existingDebt.TotalDebt = sale.TotalAmount;
                        existingDebt.RemainingDebt = sale.TotalAmount - sale.PaidAmount;
                        existingDebt.Status = existingDebt.RemainingDebt > 0 ? DebtStatus.Open : DebtStatus.Closed;
                        _unitOfWork.Debts.Update(existingDebt);
                    }
                }
            }
            // 3. TotalAmount 0 bo'lsa (hali mahsulotlar qo'shilgan yo'q), status Draft da qoladi
            else
            {
                _logger.LogWarning("Unhandled case for sale {SaleId}: TotalAmount={TotalAmount}, PaidAmount={PaidAmount}",
                    sale.Id, sale.TotalAmount, sale.PaidAmount);
            }

            // Sotuv endi yakunlandimi? (Draft → Paid/Debt/Closed). Shu bo'lsa
            // har liniya uchun Продажа harakati yoziladi — quyidagi SaveChanges
            // bilan bir tranzaksiyada (atomik).
            if (startedAsDraft && sale.Status != SaleStatus.Draft && sale.Status != SaleStatus.Cancelled)
            {
                await _stockLedger.RecordSaleFinalizationAsync(sale, cancellationToken);

                // Katalogda yo'q tovar uchun qo'shni do'konga berilgan naqd.
                // Aynan shu shartda: sotuv Draft'dan bir marta chiqadi, ya'ni
                // keyingi qisman to'lovlarda takror yozilmaydi. Qarzga sotilganda
                // ham yoziladi — mijoz keyin to'laydi, qo'shniga esa pul
                // allaqachon berilgan.
                await _externalPayouts.RecordAsync(sale, cancellationToken);
            }

            _unitOfWork.Sales.Update(sale);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Sale {SaleId} final status: {Status}", sale.Id, sale.Status);

            // To'liq chegirma qo'yilgan chek nol to'lov bilan yopiladi — unda
            // Payment qatori umuman bo'lmaydi va yozib qo'yadigan pul harakati
            // ham yo'q.
            var primary = payments.Count > 0 ? payments[0] : null;
            if (primary is not null)
                await _auditLogService.LogPaymentActionAsync(primary.Id, sale.SellerId, cancellationToken);

            return Result.Success(new PaymentDto(
                primary?.Id ?? Guid.Empty,
                // A split has no single tender type — report it as "mixed" so the
                // client does not label the whole sale by its first instalment.
                payments.Count > 1 ? "mixed" : primary?.PaymentType.ToString().ToLowerInvariant() ?? "none",
                totalTendered,
                // Nol to'lovda Payment qatori yo'q — vaqt chekning o'zidan
                // olinadi (SaveChanges uni shu tranzaksiyada belgilagan).
                primary?.CreatedAt ?? sale.UpdatedAt,
                sale.Status.ToString().ToLowerInvariant(), // Yangilangan sale status
                sale.PaidAmount, // Yangilangan paid amount
                sale.TotalAmount // Total amount
            ));
        }, cancellationToken);
    }
}
