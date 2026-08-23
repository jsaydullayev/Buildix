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
    private static void Main()
    {
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

        ApplicationConfiguration.Initialize();

        // Bitta Job Object ikkala bola jarayonni ham ushlab turadi: ilova
        // qanday tugasa ham baza va API orqada qolmaydi.
        using var job = new SafeJob();
        var port = ApiHost.FindFreePort(PreferredPort);

        try
        {
            var secrets = new LocalSecrets();
            var api = new ApiHost(port, job);
            var db = new PostgresHost(job);

            Application.Run(new MainForm(api, db, secrets));

            // Tozalash aynan shu yerda: Application.Run qaytgan, ya'ni oyna
            // yopilgan va endi kutish mumkin. Tartib muhim — avval API, keyin
            // baza: teskarisi bo'lsa API yopilayotgan bazaga so'rov yuborib
            // xato yozardi. Toza yopilmasa PostgreSQL keyingi kirishda
            // tiklash jurnalini o'qiydi va ishga tushish sekinlashadi.
            api.DisposeAsync().AsTask().GetAwaiter().GetResult();
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Buildix", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
