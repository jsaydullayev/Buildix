namespace Buildix.Domain.Enums;

/// <summary>
/// Payment method. Persisted as <c>integer</c> (AppDbContext: HasConversion&lt;int&gt;),
/// so the explicit backing values are the DB contract — never reorder or reuse
/// a number; only append new members with the next free value.
/// </summary>
public enum PaymentType
{
    Cash = 0,
    Terminal = 1,
    Transfer = 2,
    Click = 3,
    // Credit applied from a customer's outstanding refund balance.
    // Tracked as a positive Payment so the credit is consumed exactly once
    // (see CustomerService.GetAvailableCreditAsync).
    Credit = 4
}
