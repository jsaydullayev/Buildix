using Velopack;

namespace Buildix.Desktop;

internal static class Program
{
    /// <summary>Sukut bo'yicha port; band bo'lsa keyingisi olinadi.</summary>
    private const int PreferredPort = 5088;

    /// <summary>
    /// Ikkinchi nusxa ochilmasin: ikkita oyna bitta bazaga ikkita API orqali
    /// tegsa, kassir qaysi oynada ishlayotganini bilmay qoladi va chek ikki
    /// joyda ochilishi mumkin.
    /// </summary>
    private const string SingleInstanceName = @"Global\Buildix.Desktop";

    [STAThread]
    private static void Main(string[] args)
    {
        // ENG BIRINCHI qator bo'lishi shart. O'rnatish, yangilanish va
        // o'chirish paytida ilova maxsus argumentlar bilan chaqiriladi va
        // shu chaqiruvlarni Velopack shu yerda ushlab qoladi. Undan keyin
        // qo'yilsa — masalan bitta nusxa qulfidan keyin — yangilanish
        // «ilova allaqachon ochiq» degan xabarga urilib to'xtab qolardi.
        //
        // AutoApplyOnStartup — aynan shu chaqiruv yangilanishni haqiqatan
        // o'rnatadi. Updater faqat paketni diskka yuklab qo'yadi; uni shu yer
        // ochib almashtiradi. Sukut bo'yicha yoqilgan bo'lsa ham ataylab
        // oshkora yozilgan: buni o'chirish ilovani har kuni yangi versiyani
        // yuklab olib, abadiy eskisida qoladigan holga keltiradi va bunda
        // hech qanday xato ham chiqmaydi — ya'ni hech kim sezmaydi.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(true)
            .Run();

        ApplicationConfiguration.Initialize();

        // Sozlash oynasi bitta nusxa qulfidan OLDIN: kassa ochiq turgan
        // paytda ham manzilni ko'rish va tuzatish kerak bo'ladi. Aks holda
        // texnik avval ilovani yopishga majbur bo'lardi — do'kon esa savdo
        // qilayotgan bo'lishi mumkin.
        if (args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase)))
        {
            RunSetup();
            return;
        }

        using var single = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show(
                "Buildix allaqachon ochiq.",
                "Buildix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Bitta Job Object ikkala bola jarayonni ham ushlab turadi: ilova
        // qanday tugasa ham baza va API orqada qolmaydi.
        using var job = new SafeJob();
        var port = ApiHost.FindFreePort(PreferredPort);

        try
        {
            var secrets = new LocalSecrets();
            var api = new ApiHost(port, job);
            var db = new PostgresHost(job);

            // Yangilanish manzili sozlamada. Bo'lmasa tekshiruv umuman
            // o'tkazilmaydi — ilova yangilanishsiz ham to'liq ishlaydi.
            var updater = new Updater(secrets.UpdateFeedUrl);

            Application.Run(new MainForm(api, db, secrets, updater));

            // Tozalash aynan shu yerda: Application.Run qaytgan, ya'ni oyna
            // yopilgan va endi kutish mumkin. Tartib muhim — avval API, keyin
            // baza: teskarisi bo'lsa API yopilayotgan bazaga so'rov yuborib
            // xato yozardi. Toza yopilmasa PostgreSQL keyingi kirishda
            // tiklash jurnalini o'qiydi va ishga tushish sekinlashadi.
            //
            // Ulanuvchi kassada ikkalasi ham umuman ishga tushmagan — bu
            // chaqiruvlar o'sha holatda hech narsa qilmaydi.
            api.DisposeAsync().AsTask().GetAwaiter().GetResult();
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Buildix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Kassa sozlamasi oynasi. Alohida ishga tushadi va hech narsa
    /// ko'tarmaydi — manzilni o'zgartirish uchun bazani ochish shart emas.
    /// </summary>
    private static void RunSetup()
    {
        try
        {
            var secrets = new LocalSecrets();
            using var form = new SetupForm(secrets);
            Application.Run(form);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Buildix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
