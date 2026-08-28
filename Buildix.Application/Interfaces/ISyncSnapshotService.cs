using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Bulut tomoni: do'konning birinchi to'ldirilishi uchun ma'lumot beradi.
/// Sabab va tuzilish — <see cref="SyncSnapshotDto"/> izohida.
/// </summary>
public interface ISyncSnapshotService
{
    /// <summary>
    /// Bitta jadvalning bir bo'lagini qaytaradi.
    /// </summary>
    /// <param name="marketId">Kalitdan aniqlangan do'kon.</param>
    /// <param name="table">Jadval nomi (<see cref="SnapshotTables"/>).</param>
    /// <param name="after">Oldingi javobdagi joy belgisi; boshida 0.</param>
    /// <param name="take">Bo'lak hajmi; chegara — <c>MaxTake</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Jadval nomi noma'lum.</exception>
    Task<SyncSnapshotDto> GetAsync(
        int marketId, string table, int after, int take, CancellationToken ct = default);
}
