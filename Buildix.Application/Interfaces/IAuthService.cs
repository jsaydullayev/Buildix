using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    // Public self-registration removed — onboarding is SuperAdmin-gated
    // (RegistrationRequests → approve, or SuperAdmin manual create).
    Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<bool> LogoutAsync(string refreshToken, Guid callerUserId, string? accessTokenJti, DateTime? accessTokenExpiry, CancellationToken cancellationToken = default);

    /// <summary>Account "Устройства и сессии" — the user's active sessions.</summary>
    Task<IReadOnlyList<DTOs.SessionDto>> GetSessionsAsync(Guid userId, string? currentRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>«Завершить все другие сессии» — revoke every session except the caller's own.</summary>
    Task<int> RevokeOtherSessionsAsync(Guid userId, string currentRefreshToken, CancellationToken cancellationToken = default);

    /// <summary>«Завершить» bitta sessiya — faqat egasining (userId) sessiyasini bekor qiladi.
    /// Topilmasa/allaqachon bekor bo'lsa false.</summary>
    Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Account "Последние входы" — the caller's recent sign-in attempts.</summary>
    Task<IReadOnlyList<DTOs.LoginHistoryDto>> GetLoginHistoryAsync(Guid userId, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Foydalanuvchining O'Z kirishlar tarixini tozalaydi; o'chirilgan
    /// qatorlar sonini qaytaradi.
    /// </summary>
    /// <remarks>
    /// Faqat CHAQIRUVCHINING o'z qatorlari — bu tarix uning shaxsiy
    /// ma'lumoti va boshqa xodimniki begona. Audit jurnaliga aloqasi yo'q:
    /// u alohida va o'zgartirilmaydi.
    /// </remarks>
    Task<int> ClearLoginHistoryAsync(Guid userId, CancellationToken cancellationToken = default);
}
