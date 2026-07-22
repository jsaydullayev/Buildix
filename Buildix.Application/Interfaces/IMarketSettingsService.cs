using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Reads/writes the current market's <see cref="MarketSettings"/> (the
/// Настройки screen) and exposes the entity to other services that enforce
/// its rules (shift-open sales, below-cost block, debt limits, withdrawal
/// approval, notifications). The row is created lazily with design defaults.
/// </summary>
public interface IMarketSettingsService
{
    Task<MarketSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<MarketSettingsDto> UpdateAsync(UpdateMarketSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enforcement helper for other services: the settings row for a market,
    /// created with defaults if it does not exist yet.
    /// </summary>
    Task<MarketSettings> GetOrCreateAsync(int marketId, CancellationToken cancellationToken = default);
}
