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

    /// <summary>
    /// Birinchi to'liq nusxani bulutdan oladi — savdolar, qoldiqlar,
    /// mijozlar, qarzlar va qolgan tarix.
    /// </summary>
    /// <remarks>
    /// <para>Aynan bir marta bajariladi va tugagani bazada belgilanadi.
    /// Uzilsa — qayerda to'xtagan bo'lsa, o'sha joydan davom etadi.</para>
    ///
    /// <para>Tortish (<see cref="PullAsync"/>) dan KEYIN chaqirilishi
    /// shart: do'kon o'z raqamini va xodimlarini faqat o'sha yerdan
    /// biladi, ularsiz esa kelgan savdolarni yozib bo'lmaydi.</para>
    /// </remarks>
    Task<ShopSeedResult> SeedAsync(CancellationToken ct = default);
}

/// <summary>Nusxa olish natijasi.</summary>
/// <param name="Completed">Nusxa to'liq olindimi (yoki allaqachon bor edi).</param>
/// <param name="Rows">Shu chaqiruvda yozilgan qatorlar soni.</param>
public record ShopSeedResult(bool Success, bool Completed, int Rows, string? Error)
{
    public static ShopSeedResult Ok(bool completed, int rows) => new(true, completed, rows, null);

    public static ShopSeedResult Failed(string error) => new(false, false, 0, error);

    /// <summary>Bajarish shart emas edi — bu xato emas.</summary>
    public static ShopSeedResult Skipped() => new(true, true, 0, null);
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
