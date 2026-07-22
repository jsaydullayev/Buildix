namespace Buildix.Domain.Exceptions;

/// <summary>
/// Thrown when an operation that requires an OPEN shift is attempted while the
/// seller has none open. Maps to 409 Conflict with code SHIFT_NOT_OPEN so the
/// client renders a "Smenani oching" prompt instead of a generic 400.
/// </summary>
public class ShiftNotOpenException : DomainException
{
    public Guid UserId { get; }

    public override int StatusCode => 409;
    public override string Code => "SHIFT_NOT_OPEN";
    public override string UserMessage => "Ochiq smena topilmadi. Avval smenani oching.";

    public ShiftNotOpenException(Guid userId)
        : base($"User '{userId}' has no open shift.")
    {
        UserId = userId;
    }
}
