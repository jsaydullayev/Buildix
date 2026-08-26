namespace Buildix.Application.Interfaces;

/// <summary>
/// Aloqaga chiqmayotgan do'konlar haqida egalariga xabar beradi.
/// Batafsil: <c>ShopOfflineAlertService</c>.
/// </summary>
public interface IShopOfflineAlertService
{
    /// <summary>Bir o'tish. Nechta do'kon haqida xabar yuborilgani qaytadi.</summary>
    Task<int> RunAsync(CancellationToken ct = default);
}
