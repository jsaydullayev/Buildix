using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// «Bu raqamlar qachongi?» — batafsil: <c>SyncFreshnessService</c>.
/// </summary>
public interface ISyncFreshnessService
{
    Task<SyncFreshnessDto> GetAsync(int marketId, CancellationToken ct = default);
}
