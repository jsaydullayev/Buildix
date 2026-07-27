using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Konsolning «Магазины» ekrani — do'kon markazidagi ro'yxat va detal.</summary>
public interface ISuperAdminStoreService
{
    Task<IReadOnlyList<SaStoreRowDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Bitta do'konning to'liq kartochkasi. O'chirilgan do'kon uchun null.</summary>
    Task<SaStoreDetailDto?> GetAsync(int marketId, CancellationToken cancellationToken = default);
}
