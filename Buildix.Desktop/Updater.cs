using Velopack;
using Velopack.Sources;

namespace Buildix.Desktop;

/// <summary>
/// Yangilanishlarni fonda tekshiradi va yuklab qo'yadi.
///
/// <para><b>Bu sinf faqat YUKLAB OLADI.</b> Qo'llashni Velopack ning o'zi
/// bajaradi: <see cref="VelopackApp"/> ishga tushishda yuklab qo'yilgan
/// paketni topsa, uni o'zi o'rnatadi (<c>SetAutoApplyOnStartup</c>, qarang
/// <c>Program.Main</c>). Shu sababli bu yerda <c>ApplyUpdatesAndRestart</c>
/// yoki <c>WaitExitThenApplyUpdates</c> chaqirilmaydi — ular ishlayotgan
/// ilovani to'xtatadi yoki yopilishda kassirga progress oynasini
/// ko'rsatadi.</para>
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
        if (string.IsNullOrWhiteSpace(_feedUrl))
        {
            RecordOutcome("manzil sozlanmagan — tekshirilmadi");
            return;
        }

        try
        {
            var manager = new UpdateManager(new SimpleWebSource(_feedUrl));
            if (!manager.IsInstalled) return;   // ishlab chiqish rejimida o'tkazamiz

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                RecordOutcome("yangilanish yo'q — eng so'nggi versiya");
                return;
            }

            await manager.DownloadUpdatesAsync(update);
            PendingVersion = update.TargetFullRelease.Version.ToString();
            RecordOutcome($"{PendingVersion} yuklab olindi — keyingi ochilishda o'rnatiladi");
        }
        catch (Exception ex)
        {
            // Internet yo'q, server javob bermadi, paket buzilgan — bularning
            // hech biri kassani to'xtatmasligi kerak.
            RecordOutcome($"muvaffaqiyatsiz: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Oxirgi tekshiruv natijasini faylga yozadi.
    ///
    /// <para><b>Nega kerak.</b> Yuqoridagi <c>catch</c> hamma xatoni yutadi va
    /// bu ataylab shunday — kassir server muammosini ko'rmasligi kerak. Lekin
    /// shu sababli yangilanish umuman kelmayotganini HECH KIM sezmaydi:
    /// do'konda xato chiqmaydi, ilova esa eski versiyada ishlayveradi. Manzil
    /// noto'g'ri yozilgan yoki serverdagi papka yopiq bo'lsa, buni aniqlashning
    /// yagona yo'li — shu fayl. Do'konga borgan yoki masofadan ulangan odam
    /// birinchi navbatda shuni ochadi.</para>
    ///
    /// <para>Fayl HAR SAFAR qayta yoziladi, ustiga qo'shilmaydi: bu yerda
    /// tarix emas, «hozir nima bo'lyapti» degan savolga javob kerak, va
    /// o'sib boradigan jurnal do'kon diskini yeb qo'yishi mumkin.</para>
    /// </summary>
    private static void RecordOutcome(string outcome)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Buildix", "update.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // BOM bilan: bu faylni do'konda Bloknot, PowerShell yoki boshqa
            // vosita ochadi va ularning bir qismi BOM'siz UTF-8 ni tizim
            // kodlashi deb o'qiydi — natijada o'zbekcha matn tanib bo'lmas
            // holga keladi. Xabar tushunarsiz bo'lsa, jurnalning ma'nosi
            // qolmaydi.
            File.WriteAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {outcome}{Environment.NewLine}",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception)
        {
            // Yozib bo'lmadi (huquq yo'q, disk to'lgan) — bu yangilanishdan
            // ham, savdodan ham muhimroq emas.
        }
    }
}
