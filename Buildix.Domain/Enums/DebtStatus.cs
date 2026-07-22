namespace Buildix.Domain.Enums;

/// <summary>Debt status. Persisted as int — explicit values are the DB contract.</summary>
public enum DebtStatus
{
    Open = 0,
    Closed = 1,
    /// Qarzdor tovarni qisman qaytargan holati
    Returned = 2
}
