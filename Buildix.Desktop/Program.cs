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

        var port = ApiHost.FindFreePort(PreferredPort);
        var api = new ApiHost(port);
        Application.Run(new MainForm(api));
    }
}
