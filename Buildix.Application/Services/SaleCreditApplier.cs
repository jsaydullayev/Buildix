using Microsoft.EntityFrameworkCore;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Shared customer-credit application, extracted from SaleService's private
/// helper so both the sale-lifecycle and item concerns apply credit identically.
/// See <see cref="ISaleCreditApplier"/>.
/// </summary>
public class SaleCreditApplier : ISaleCreditApplier
{
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly ICustomerService _customerService;
    private readonly ILogger<SaleCreditApplier> _logger;

    public SaleCreditApplier(
        IAppDbContext context,
        ICurrentMarketService currentMarketService,
        ICustomerService customerService,
        ILogger<SaleCreditApplier> logger)
    {
        _context = context;
        _currentMarketService = currentMarketService;
        _customerService = customerService;
        _logger = logger;
    }

    public async Task ApplyAsync(Guid saleId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var availableCredit = await _customerService.GetAvailableCreditAsync(customerId, cancellationToken);

        if (availableCredit <= 0)
            return;

        var sale = await _context.Sales
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == saleId && s.MarketId == marketId, cancellationToken);

        if (sale == null)
            return;

        var creditToApply = Math.Min(availableCredit, sale.TotalAmount - sale.PaidAmount);

        if (creditToApply <= 0)
            return;

        _logger.LogInformation("Applying customer credit: SaleId={SaleId}, CustomerId={CustomerId}, CreditToApply={CreditToApply}, AvailableCredit={AvailableCredit}",
            saleId, customerId, creditToApply, availableCredit);

        // Record credit consumption as a positive Payment with PaymentType.Credit.
        // GetAvailableCreditAsync subtracts these from the refund balance so the same
        // credit cannot be spent twice.
        var creditPayment = new Payment
        {
            Id = Guid.NewGuid(),
            SaleId = saleId,
            MarketId = marketId,
            PaymentType = PaymentType.Credit,
            Amount = creditToApply,
            CreatedAt = DateTime.UtcNow
        };
        await _context.Payments.AddAsync(creditPayment, cancellationToken);

        sale.PaidAmount += creditToApply;
        _context.Sales.Update(sale);

        var debtForCredit = await _context.Debts
            .FirstOrDefaultAsync(d => d.SaleId == saleId && d.MarketId == marketId, cancellationToken);
        if (debtForCredit != null)
        {
            debtForCredit.RemainingDebt = Math.Max(0, debtForCredit.RemainingDebt - creditToApply);
            if (debtForCredit.RemainingDebt <= 0)
                debtForCredit.Status = DebtStatus.Closed;
            _context.Debts.Update(debtForCredit);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Credit applied successfully: SaleId={SaleId}, NewPaidAmount={NewPaidAmount}",
            saleId, sale.PaidAmount);
    }
}
