using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Konsolning «Пользователи платформы» ekrani — barcha do'konlar xodimlari.
/// Faqat platformaga KIRISH boshqariladi (parol, hisob holati); rollar va
/// do'kon ichidagi ruxsatlar egasi/administratori ishi.
/// </summary>
public interface ISuperAdminUserService
{
    Task<PagedResult<SaUserRowDto>> ListAsync(
        string? role, int? marketId, string? search, int page, int size, CancellationToken ct = default);

    /// <summary>
    /// Yangi parol qo'yadi va foydalanuvchining BARCHA sessiyalarini uzadi.
    /// Foydalanuvchi topilmasa false.
    /// </summary>
    Task<bool> ResetPasswordAsync(Guid userId, string newPassword, Guid superAdminUserId, CancellationToken ct = default);

    /// <summary>Hisobni yoqadi/o'chiradi. O'chirishda sessiyalar uziladi.</summary>
    Task<bool> SetActiveAsync(Guid userId, bool active, Guid superAdminUserId, CancellationToken ct = default);
}
