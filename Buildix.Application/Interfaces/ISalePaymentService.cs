using Buildix.Application.DTOs;
using Buildix.Application.Common;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Payment concern extracted from the former SaleService god-class. Owns
/// recording a payment against a sale and the resulting status / debt / cash-
/// register transitions. Kept as its own service so the payment state machine
/// (the money path) has one focused home rather than being buried in a
/// ~2000-line class.
/// </summary>
public interface ISalePaymentService
{
    Task<Result<PaymentDto>> AddPaymentAsync(Guid saleId, AddPaymentDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Close a sale with one or more tenders ("Микс") in a single transaction.
    /// Shares the money path with <see cref="AddPaymentAsync"/>, which is just
    /// this with a one-element list. A split cannot be done as two AddPayment
    /// calls: the first partial tender is rejected on a walk-in sale and, with a
    /// customer, transiently marks the sale as debt between the calls.
    /// </summary>
    Task<Result<PaymentDto>> CheckoutAsync(Guid saleId, CheckoutSaleDto request, CancellationToken cancellationToken = default);
}
