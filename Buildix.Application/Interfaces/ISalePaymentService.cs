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
}
