namespace Buildix.Domain.Enums;

/// <summary>Sale lifecycle. Persisted as int — explicit values are the DB contract.</summary>
public enum SaleStatus
{
    Draft = 0,
    Paid = 1,
    Debt = 2,
    Closed = 3,
    Cancelled = 4
}
