using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Bulutdan do'konga tushadigan ma'lumot. Batafsil: <c>SyncPullService</c>.
/// </summary>
public interface ISyncPullService
{
    /// <summary>
    /// <paramref name="since"/> dan keyin o'zgargan hamma narsani qaytaradi.
    /// Birinchi so'rovda do'kon uzoq o'tmishdagi sanani yuboradi va butun
    /// holatni oladi.
    ///
    /// <para>Vaqt <see cref="DateTimeOffset"/>: sinxronizatsiya kanali vaqt
    /// mintaqasiga bog'liq bo'lmasligi SHART — sabab <c>SyncPullDto</c> da.</para>
    /// </summary>
    Task<SyncPullDto> PullAsync(int marketId, DateTimeOffset since, CancellationToken ct = default);
}
