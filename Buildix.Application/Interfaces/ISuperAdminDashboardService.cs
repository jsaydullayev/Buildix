using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Platforma paneli uchun yagona snapshot (SuperAdmin konsoli).</summary>
public interface ISuperAdminDashboardService
{
    Task<SaDashboardDto> GetAsync(CancellationToken cancellationToken = default);
}
