namespace Buildix.Application.Interfaces;

/// <summary>
/// Applies a customer's available credit (accrued from refunds / negative
/// payments) against an open sale. Shared by SaleService (create / customer-
/// change / explicit apply) and SaleItemService (re-apply after the bill grows),
/// so credit consumption is recorded one way — a positive Credit payment plus a
/// debt-remaining adjustment — and the same credit can never be spent twice.
/// </summary>
public interface ISaleCreditApplier
{
    Task ApplyAsync(Guid saleId, Guid customerId, CancellationToken cancellationToken = default);
}
