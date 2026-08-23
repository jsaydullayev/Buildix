using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Buildix.Desktop;

/// <summary>
/// Ilova oynasi: API ni ko'taradi va interfeysni ko'rsatadi.
///
/// <para><b>Nega holat ekrani bor.</b> API ko'tarilishi bir necha soniya
/// oladi (migratsiyalar, baza ulanishi). Oynani darhol WebView bilan ochsak,
/// omborchi bir necha soniya BO'SH OQ EKRAN ko'radi va ilova ishlamayapti
/// deb o'ylaydi. Shuning uchun avval holat matni ko'rinadi va interfeys
/// faqat tayyor bo'lganda almashadi.</para>
/// </summary>
public sealed class MainForm : Form
{
    private readonly ApiHost _api;
    private readonly PostgresHost _db;
    private readonly LocalSecrets _secrets;
    private readonly Label _status;
    private WebView2? _web;

    public MainForm(ApiHost api, PostgresHost db, LocalSecrets secrets)
    {
        _api = api;
        _db = db;
        _secrets = secrets;

        Text = "Buildix";
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1024, 640);
        BackColor = Color.FromArgb(0x0F, 0x25, 0x57);   // brend navy — oq miltillash bo'lmasin
        StartPosition = FormStartPosition.CenterScreen;

        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0x0F, 0x25, 0x57),
            Font = new Font("Segoe UI", 11F),
            Text = "Buildix ishga tushmoqda…",
        };
        Controls.Add(_status);

        Shown += async (_, _) => await StartAsync();
        FormClosed += async (_, _) =>
        {
            // Tartib muhim: avval API, keyin baza. Teskarisi bo'lsa API
            // yopilayotgan bazaga so'rov yuborib xato yozardi.
            await _api.DisposeAsync();
            await _db.DisposeAsync();
        };
    }

    private async Task StartAsync()
    {
        if (!_api.ApiExists)
        {
            Fail("Buildix.API topilmadi.\n\nIlova to'liq o'rnatilmagan bo'lishi mumkin — "
                 + "o'rnatuvchini qaytadan ishga tushiring.");
            return;
        }

        // ── Baza ──────────────────────────────────────────────────────────
        if (_db.IsBundled)
        {
            _status.Text = "Ma'lumotlar bazasi ishga tushmoqda…";
            string? dbError;
            try
            {
                dbError = await _db.StartAsync(
                    key => _secrets.GetOrCreate(key, PostgresHost.NewPassword),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                dbError = ex.Message;
            }
            if (dbError is not null) { Fail(dbError); return; }
            _api.ConnectionString = _db.ConnectionString;
        }

        try
        {
            _api.Start();
        }
        catch (Exception ex)
        {
            Fail("Buildix.API ishga tushmadi.\n\n" + ex.Message);
            return;
        }

        _status.Text = "Ma'lumotlar bazasi tayyorlanmoqda…";
        var error = await _api.WaitUntilReadyAsync(TimeSpan.FromSeconds(90), CancellationToken.None);
        if (error is not null)
        {
            Fail(error + "\n\nEng ko'p uchraydigan sabab — PostgreSQL xizmati ishlamayapti.");
            return;
        }

        await ShowInterfaceAsync();
    }

    private async Task ShowInterfaceAsync()
    {
        _status.Text = "Interfeys yuklanmoqda…";

        // WebView2 ma'lumotlari foydalanuvchi profilida saqlanadi: Program Files
        // ga yozish huquqi bo'lmasligi mumkin va ilova umuman ochilmasdi.
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Buildix", "WebView2");
        Directory.CreateDirectory(dataFolder);

        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        }
        catch (Exception ex)
        {
            Fail("WebView2 komponenti topilmadi yoki ishga tushmadi.\n\n"
                 + "Windows'da «Microsoft Edge WebView2 Runtime» o'rnatilgan bo'lishi kerak.\n\n"
                 + ex.Message);
            return;
        }

        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);
        _web.BringToFront();
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        // Kassada kerak emas va tasodifan bosilishi mumkin bo'lgan narsalar.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        // Tashqi havolalar ilova oynasini almashtirmasin — tizim brauzerida ochilsin.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternally(e.Uri);
        };

        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                _status.Text = "Interfeysni yuklab bo'lmadi. Ilovani qaytadan oching.";
            else
                _status.Visible = false;
        };

        _web.Source = new Uri(_api.BaseUrl);
    }

    private static void OpenExternally(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Brauzer topilmasa — jimgina o'tamiz, bu sotuvni to'xtatmasligi kerak.
        }
    }

    private void Fail(string message)
    {
        _status.Text = message;
        _status.Visible = true;
        _web?.Hide();
    }
}
