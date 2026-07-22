namespace Buildix.Domain.Exceptions;

/// <summary>
/// Thrown when creating a staff account with a username that already exists in
/// the caller's market (enforced by IX_Users_MarketId_Username_Unique). Maps to
/// 409 Conflict with code USERNAME_TAKEN so the client warns inline on the
/// username field instead of showing a generic 400.
/// </summary>
public class DuplicateUsernameException : DomainException
{
    public string Username { get; }

    public override int StatusCode => 409;
    public override string Code => "USERNAME_TAKEN";
    public override string UserMessage => "Bu foydalanuvchi nomi allaqachon band. Boshqa nom tanlang.";

    public DuplicateUsernameException(string username)
        : base($"Username '{username}' already exists")
    {
        Username = username;
    }
}
