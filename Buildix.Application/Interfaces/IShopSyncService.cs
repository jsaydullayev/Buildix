namespace Buildix.Application.Interfaces;

/// <summary>
/// Do'kon nusxasi tomonidagi sinxronizatsiya. Batafsil: <c>ShopSyncService</c>.
/// </summary>
public interface IShopSyncService
{
    /// <summary>Bulut manzili va kaliti berilganmi.</summary>
    bool IsConfigured { get; }

    /// <summary>Bulutdan o'zgarishlarni olib, lokal bazaga yozadi.</summary>
    Task<ShopSyncResult> PullAsync(CancellationToken ct = default);
}

/// <summary>
/// Tortish natijasi.
///
/// <para>Istisno o'rniga natija qaytariladi: internet yo'qligi do'konda
/// NORMAL holat va uni istisno bilan belgilash chaqiruvchini har safar
/// «bu xatomi yoki oddiy holatmi» degan savolga majbur qilardi.</para>
/// </summary>
public record ShopSyncResult(bool Success, bool MarketChanged, int UserCount, string? Error)
{
    public static ShopSyncResult Ok(bool marketChanged, int userCount) =>
        new(true, marketChanged, userCount, null);

    public static ShopSyncResult Failed(string error) => new(false, false, 0, error);

    /// <summary>Bulut sozlanmagan — bu xato emas.</summary>
    public static ShopSyncResult Skipped(string reason) => new(true, false, 0, reason);
}

/// <summary>
/// Do'kon nusxasining bulut sozlamasi.
///
/// <para>Qiymatlar qobiqdan MUHIT O'ZGARUVCHISI orqali keladi, fayldan emas:
/// kalit API o'qiydigan sozlama faylida ochiq yotmasligi kerak. Sirlar
/// faylining yagona egasi — qobiq.</para>
/// </summary>
public sealed class ShopCloudOptions
{
    public string? Url { get; init; }
    public string? TerminalKey { get; init; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(TerminalKey);
}
