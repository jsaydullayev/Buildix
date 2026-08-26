namespace Buildix.Application.Interfaces;

/// <summary>Do'kon tomoni: o'zgargan yozuvlarni bulutga yuboradi.</summary>
public interface IShopPushService
{
    bool IsConfigured { get; }

    Task<ShopPushResult> PushAsync(CancellationToken ct = default);
}

/// <summary>
/// Yuborish natijasi. Istisno o'rniga natija: aloqa uzilishi do'konda NORMAL
/// holat va u savdoni to'xtatmasligi kerak.
/// </summary>
public record ShopPushResult(bool Success, int Rows, string? Error)
{
    public static ShopPushResult Ok(int rows) => new(true, rows, null);
    public static ShopPushResult Failed(string error) => new(false, 0, error);
    public static ShopPushResult Skipped(string reason) => new(true, 0, reason);
}
