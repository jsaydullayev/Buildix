using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Konsolning «Настройки платформы» ekrani.</summary>
public interface ISuperAdminSettingsService
{
    Task<SaSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Saqlaydi va sozlamalar keshini yangilaydi (obuna eshigi shu keshdan
    /// o'qiydi). Noto'g'ri kombinatsiyada <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<SaSettingsDto> UpdateAsync(SaUpdateSettingsDto dto, Guid superAdminUserId, CancellationToken ct = default);
}
