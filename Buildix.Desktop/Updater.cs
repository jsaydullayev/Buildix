using Velopack;
using Velopack.Sources;

namespace Buildix.Desktop;

/// <summary>
/// Yangilanishlarni fonda tekshiradi va yuklab qo'yadi.
///
/// <para><b>Nega hech qachon o'zi qayta ishga tushmaydi.</b> Kassada
/// yangilanish savdoni to'xtatishi mumkin emas: mijoz turibdi, savatda tovar
/// bor. Shuning uchun yangi versiya jimgina yuklab olinadi va faqat ilova
/// KEYINGI safar ochilganda qo'llanadi. Omborchi hech narsa sezmaydi —
/// ertalab ilovani ochganda yangi versiya turadi.</para>
///
/// <para><b>Nega tekshiruv jimgina.</b> Do'kon internetsiz ishlashi mumkin va
/// bu normal holat. Yangilanish serveriga chiqib bo'lmasa — bu xato emas,
/// shunchaki hozir yangilanish yo'q. Ekranga xato chiqarish omborchini
/// bezovta qilardi va u har kuni «internet yo'q» degan xabarni ko'rardi.</para>
/// </summary>
public sealed class Updater
{
    private readonly string? _feedUrl;

    public Updater(string? feedUrl) => _feedUrl = feedUrl;

    /// <summary>Yuklab qo'yilgan versiya bor bo'lsa — uning raqami.</summary>
    public string? PendingVersion { get; private set; }

    /// <summary>
    /// Fonda tekshiradi va topilsa yuklab qo'yadi. Hech qanday holatda
    /// istisno tashlamaydi: yangilanish savdodan muhimroq emas.
    /// </summary>
    public async Task CheckAsync()
    {
        if (string.IsNullOrWhiteSpace(_feedUrl)) return;

        try
        {
            var manager = new UpdateManager(new SimpleWebSource(_feedUrl));
            if (!manager.IsInstalled) return;   // ishlab chiqish rejimida o'tkazamiz

            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;

            await manager.DownloadUpdatesAsync(update);
            PendingVersion = update.TargetFullRelease.Version.ToString();
        }
        catch (Exception)
        {
            // Internet yo'q, server javob bermadi, paket buzilgan — bularning
            // hech biri kassani to'xtatmasligi kerak.
        }
    }
}
