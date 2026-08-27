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
    private readonly Updater _updater;
    private readonly Label _status;
    private WebView2? _web;
    private Button? _setup;

    public MainForm(ApiHost api, PostgresHost db, LocalSecrets secrets, Updater updater)
    {
        _api = api;
        _db = db;
        _secrets = secrets;
        _updater = updater;

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

        // Ushlanmagan istisno bu yerda ilovani JIMGINA yopib yuborardi:
        // `async void` ichidagi xato hech qayerga chiqmaydi va omborchi
        // faqat oyna g'oyib bo'lganini ko'rardi.
        Shown += async (_, _) =>
        {
            try
            {
                await StartAsync();
            }
            catch (Exception ex)
            {
                Fail("Kutilmagan xato." + Environment.NewLine + Environment.NewLine + ex.Message);
            }
        };
        // Tozalash bu yerda EMAS: FormClosed — `async void`, WinForms uni
        // kutmaydi va jarayon tugab ketadi. Natijada PostgreSQL toza
        // yopilmasdi va har kirishda tiklash jurnali o'qilardi. Tozalash
        // Program.Main da, Application.Run qaytgandan keyin bajariladi.
    }

    private async Task StartAsync()
    {
        // ── Ulanuvchi kassa ───────────────────────────────────────────────
        // Bu kompyuterda na baza, na API ko'tariladi: ikkalasi ham server
        // kassada. Shu sababli u sezilarli tez ochiladi — migratsiya ham,
        // PostgreSQL ham kutilmaydi.
        if (_secrets.ServerUrl is { } serverUrl)
        {
            _status.Text = "Server kassaga ulanmoqda…";
            var problem = await ServerProbe.CheckAsync(serverUrl, CancellationToken.None);
            if (problem is not null)
            {
                FailWithSetup(problem, serverUrl);
                return;
            }

            await ShowInterfaceAsync(serverUrl);
            WatchServer(serverUrl);
            return;
        }

        if (!_api.ApiExists)
        {
            Fail("Buildix.API topilmadi.\n\nIlova to'liq o'rnatilmagan bo'lishi mumkin — "
                 + "o'rnatuvchini qaytadan ishga tushiring.");
            return;
        }

        // ── Baza ──────────────────────────────────────────────────────────
        // To'plamda PostgreSQL bo'lmasa DARHOL to'xtaymiz. Ilgari bu holat
        // jimgina o'tib ketardi: API ulanish satrisiz ishga tushar va
        // sozlamadagi zaxira qiymatga (ishlab chiqish bazasi) urinardi.
        // Dasturchining kompyuterida bunday baza bor, shuning uchun hammasi
        // ishlayotgandek ko'rinardi — do'konda esa ilova tushunarsiz xato
        // bilan ochilmasdi.
        if (!_db.IsBundled)
        {
            Fail("Ma'lumotlar bazasi topilmadi.\n\n"
                 + "To'plam to'liq emas — PostgreSQL ilova bilan birga kelmagan.\n"
                 + "O'rnatuvchini qaytadan yuklab, qayta o'rnating.");
            return;
        }

        _status.Text = "Ma'lumotlar bazasi ishga tushmoqda…";
        string? dbError;
        try
        {
            dbError = await _db.StartAsync(
                key => _secrets.GetOrCreate(key, PostgresHost.NewPassword),
                _secrets.CreatedNow,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }
        if (dbError is not null) { Fail(dbError); return; }
        _api.ConnectionString = _db.ConnectionString;

        // ── Bulutga bog'lanish ────────────────────────────────────────────
        // Kalit yo'q bo'lsa do'kon bazasida xodim ham yo'q va kirish
        // oynasidan nariga o'tib bo'lmaydi. Shu sababli oyna AYNAN shu
        // yerda ko'rsatiladi: baza tayyor, lekin API hali ishga
        // tushirilmagan — kalit unga ishga tushishda beriladi va qayta
        // ishga tushirish kerak bo'lmaydi.
        if (_secrets.TerminalKey is null)
        {
            _status.Text = "Do'konni bulutga bog'lash…";
            using var pairing = new PairingForm(_secrets);
            pairing.ShowDialog(this);
        }
        _api.CloudUrl = _secrets.CloudUrl;
        _api.TerminalKey = _secrets.TerminalKey;

        try
        {
            _api.AllowLan = _secrets.AllowLan;
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
            // Jurnal yo'lini ko'rsatamiz: usiz do'kondan «ochilmayapti»
            // degandan boshqa hech qanday ma'lumot kelmasdi.
            Fail(error + "\n\nBatafsil: " + ApiHost.LogPath);
            return;
        }

        await ShowInterfaceAsync(_api.BaseUrl);
    }

    private async Task ShowInterfaceAsync(string baseUrl)
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

        // Yorliq printeri — sahifa chop etish oynasini ochmasdan bossin.
        // Printer sozlanmagan bo'lsa ko'prik xato qaytaradi va sahifa
        // odatdagi yo'lga tushadi, ya'ni ish to'xtamaydi.
        new LabelPrintBridge(_secrets, env).Attach(core);

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

        // ── Nega ildiz EMAS, `/login` ─────────────────────────────────────
        // Ildizda reklama sahifasi turadi — narxlar, «bog'lanish», tanishtiruv.
        // U bulutdagi TASHRIFCHI uchun. Do'kon kompyuterida esa faqat
        // ishlaydigan odam o'tiradi va unga birinchi ko'rinadigan narsa ish
        // ekrani bo'lishi kerak, sotuv sahifasi emas.
        //
        // Sessiya hali yaroqli bo'lsa `/login` o'zi ish ekraniga o'tkazadi,
        // ya'ni har ochilishda parol so'ralmaydi.
        _web.Source = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "login");

        // Yangilanish FONDA tekshiriladi — ochilishni kechiktirmaydi va
        // internetsiz do'konda hech narsa o'zgarmaydi.
        _ = CheckUpdateAsync();

        // Kunlik zaxira nusxa ham fonda: interfeys allaqachon ekranda va
        // kassir savdoni boshlayveradi. Ulanuvchi kassada baza yo'q, shuning
        // uchun u yerda bu chaqiruv o'tkazib yuboriladi.
        if (_secrets.ServerUrl is null)
            _ = _db.BackupIfDueAsync(CancellationToken.None);
    }

    /// <summary>
    /// Yangi versiya yuklab qo'yilgan bo'lsa sarlavhada bildiradi.
    ///
    /// <para>Qayta ishga tushishni TAKLIF QILMAYMIZ: kassir savdo o'rtasida
    /// bo'lishi mumkin va «hozir yangilansinmi?» degan oyna aynan noto'g'ri
    /// paytda chiqadi. Yangi versiya ilova keyingi safar ochilganda o'zi
    /// qo'llanadi.</para>
    /// </summary>
    private async Task CheckUpdateAsync()
    {
        await _updater.CheckAsync();
        if (_updater.PendingVersion is null || IsDisposed) return;

        BeginInvoke(() =>
        {
            Text = $"Buildix — {_updater.PendingVersion} versiyasi keyingi ochilishda o'rnatiladi";
        });
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

    /// <summary>
    /// Server topilmaganda ko'rsatiladigan ekran — sababi va uni TUZATISH
    /// tugmasi bilan.
    ///
    /// <para><b>Nega tugma kerak.</b> Eng ko'p uchraydigan sabab — server
    /// kompyuterning IP manzili o'zgargani (router qayta yoqilgan). Tugmasiz
    /// kassir faqat «ulanib bo'lmadi» degan yozuvni ko'rar va do'kon savdo
    /// qila olmay qolardi — texnik kelguncha.</para>
    /// </summary>
    private void FailWithSetup(string problem, string serverUrl)
    {
        Fail($"Server kassaga ulanib bo'lmadi.\n\n{problem}\n\nManzil: {serverUrl}");

        if (_setup is not null) return;

        _setup = new Button
        {
            Text = "Sozlamani ochish",
            Size = new Size(190, 40),
            Anchor = AnchorStyles.None,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10F),
        };
        _setup.Location = new Point((ClientSize.Width - _setup.Width) / 2, ClientSize.Height / 2 + 60);
        _setup.Click += (_, _) =>
        {
            using var form = new SetupForm(_secrets);
            if (form.ShowDialog(this) != DialogResult.OK) return;

            // Sozlama o'zgargach ilovani qayta ochish kerak: rol (server yoki
            // ulanuvchi) ishga tushishda hal bo'ladi va uni yo'l-yo'lakay
            // almashtirish yarim holatlar keltirib chiqarardi.
            MessageBox.Show(
                "Saqlandi. O'zgarish kuchga kirishi uchun Buildix'ni qaytadan oching.",
                "Buildix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        };
        Controls.Add(_setup);
        _setup.BringToFront();
    }

    /// <summary>
    /// Server kassa bilan aloqani kuzatib turadi.
    ///
    /// <para><b>Nega kerak.</b> Server o'chsa yoki tarmoq uzilsa, WebView2
    /// brauzerning ingliz tilidagi «This site can't be reached» sahifasini
    /// ko'rsatardi. Kassir uchun bu hech narsa anglatmaydi va u ilova
    /// buzilgan deb o'ylaydi.</para>
    ///
    /// <para><b>Nega o'zi qaytadan ulanadi.</b> Uzilish odatda qisqa — router
    /// qayta yoqiladi, kabel qimirlatiladi. Aloqa tiklangach sahifa o'zi
    /// yangilanadi va savdo davom etadi; kassirdan hech narsa talab
    /// qilinmaydi.</para>
    /// </summary>
    private void WatchServer(string serverUrl)
    {
        var offline = false;

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) =>
        {
            if (IsDisposed) { timer.Stop(); return; }

            var problem = await ServerProbe.CheckAsync(serverUrl, CancellationToken.None);

            if (problem is not null && !offline)
            {
                offline = true;
                Fail("Server kassa bilan aloqa uzildi.\n\n" + problem
                     + "\n\nAloqa tiklanishi bilan ish o'zi davom etadi.");
            }
            else if (problem is null && offline)
            {
                offline = false;
                _status.Visible = false;
                _web?.Show();
                // Uzilish paytida yuborilgan so'rovlar yo'qolgan bo'lishi
                // mumkin, shuning uchun sahifa qayta yuklanadi — yarim
                // to'ldirilgan ekran bilan ishlashdan ko'ra shu xavfsiz.
                _web?.CoreWebView2?.Reload();
            }
        };
        timer.Start();
        Disposed += (_, _) => timer.Dispose();
    }
}
