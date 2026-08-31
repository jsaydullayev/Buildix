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
            var probe = await ServerProbe.ProbeAsync(serverUrl, CancellationToken.None);

            // Manzilda BOSHQA do'kon turgan bo'lsa ulanmaymiz. Router IP ni
            // qayta tarqatganda eski manzil boshqa kompyuterga o'tishi
            // mumkin; bitta tarmoqda ikkinchi Buildix bo'lsa kassa jimgina
            // begona bazaga ulanib ketardi va savdolar o'sha do'konga
            // yozilardi. Bu — qaytarib bo'lmaydigan zarar, shuning uchun
            // ulanishdan OLDIN to'xtatiladi.
            if (ServerProbe.ShopMismatch(_secrets.ServerShopId, probe) is { } mismatch)
            {
                FailWithSetup(mismatch, serverUrl);
                return;
            }

            // Belgi hali yozilmagan bo'lsa — sozlashda «Tekshirish» bosilmagan
            // yoki sozlama eski versiyadan qolgan — birinchi muvaffaqiyatli
            // ulanishda yozib qo'yamiz. Shundan keyin manzil boshqa
            // kompyuterga o'tsa, kassa buni darhol sezadi.
            RememberShop(probe);

            var problem = probe.Problem;
            if (problem is not null)
            {
                // Birinchi urinish yiqildi — lekin bu YAKUNIY javob emas.
                //
                // Ertalab do'konda ikkala kompyuter bitta uzatgichdan bir
                // vaqtda yonadi. Server kassada PostgreSQL toza yopilmagani
                // uchun tiklanish jurnalini o'qiydi va API bir daqiqagacha
                // javob bermasligi mumkin. Ulanuvchi kassa esa to'rt soniyada
                // so'raydi. Ilgari shu yerda TO'XTAB qolardi va ekranda
                // «Server javob bermadi» yozuvi bilan abadiy turardi —
                // server ko'tarilgandan keyin ham. Kassir ilovani qo'lda
                // qayta ochishi kerak edi, buni esa unga hech kim aytmagan.
                FailWithSetup(problem, serverUrl);
                RetryUntilServerUp(serverUrl);
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
        // Ulanuvchi kassalar shu belgi bilan «to'g'ri do'konmi?» degan
        // savolga javob oladi.
        _api.ShopId = _secrets.ShopId;

        _api.AllowLan = _secrets.AllowLan;

        // Tarmoqqa ochilgan kassada port QAT'IY: boshqa kassalar unga aynan
        // shu raqam bilan ulanadi. Band bo'lsa buni ochiq aytamiz — jimgina
        // boshqa portga o'tish 2-kassani ulanolmas holga keltirar va sabab
        // hech qayerda ko'rinmasdi.
        if (_api.AllowLan && !ApiHost.IsPortFree(_api.Port, lan: true))
        {
            Fail($"{_api.Port}-port band.\n\n"
                 + "Boshqa kassalar shu kompyuterga AYNAN shu port orqali ulanadi, "
                 + "shuning uchun uni almashtirib bo'lmaydi.\n\n"
                 + "Portni band qilgan dasturni yoping. Yoki bu kompyuterga boshqa "
                 + "kassalar ulanmasa, sozlashda «Boshqa kassalar shu kompyuterga "
                 + "ulanadi» bandini o'chiring (Buildix.Desktop.exe --setup).");
            return;
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

        // Yorliq va chek printeri — sahifa chop etish oynasini ochmasdan
        // bossin. KUTILADI: ko'prik sahifaga o'zini tanitmasdan turib
        // manzil qo'yilsa, birinchi hujjat qobiqni ko'rmay qolar va chek
        // brauzer yo'liga tushardi.
        await new LabelPrintBridge(_secrets, env).AttachAsync(core);

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
        // Manzil sozlanmagan bo'lsa yangilanish umuman tekshirilmaydi va bu
        // JIMGINA o'tib ketardi: xato chiqmasdi, sarlavha o'zgarmasdi. Texnik
        // o'rnatishda uni yozishni unutsa, o'sha do'kon abadiy eski versiyada
        // qolardi va buni bilishning yagona yo'li yo'q edi.
        //
        // Kassirga tegishli xabar emas, shuning uchun u ogohlantirish oynasi
        // emas — sarlavhada turadi va sozlashni ochgan odam darhol ko'radi.
        if (_secrets.UpdateFeedUrl is null)
        {
            if (!IsDisposed)
                BeginInvoke(() => Text = "Buildix — yangilanish manzili sozlanmagan (--setup)");
            return;
        }

        await _updater.CheckAsync();
        if (_updater.PendingVersion is null || IsDisposed) return;

        BeginInvoke(() =>
        {
            Text = $"Buildix — {_updater.PendingVersion} versiyasi keyingi ochilishda o'rnatiladi";
        });
    }

    /// <summary>
    /// Tashqi havolani tizim brauzerida ochadi.
    /// </summary>
    /// <remarks>
    /// <para><b>Faqat haqiqiy havolalar.</b> Sahifa ichida yaratilgan
    /// <c>blob:</c> va <c>data:</c> manzillari brauzer XOTIRASIDA yashaydi
    /// va undan tashqarida umuman mavjud emas. Ilgari ular ham shu yerga
    /// tushar va Windows kassirga «bu 'blob' havolasini ochadigan dastur
    /// yo'q, Microsoft Store'dan qidiring» degan oyna chiqarardi — chek
    /// chiqarilayotgan payt, kassa oldida navbat turganda.</para>
    ///
    /// <para><c>file:</c> ham chiqarib tashlangan: sahifa ixtiyoriy faylni
    /// ochtira olmasligi kerak.</para>
    /// </remarks>
    /// <summary>
    /// Do'kon belgisini birinchi muvaffaqiyatli ulanishda yozib qo'yadi.
    /// </summary>
    /// <remarks>
    /// Mavjud belgi HECH QACHON ustiga yozilmaydi: aks holda begona serverga
    /// ulanish uni jimgina «to'g'ri» deb qayd etar va butun himoya ma'nosiz
    /// bo'lardi. Mos kelmaslik allaqachon yuqorida to'xtatilgan.
    /// </remarks>
    private void RememberShop(ServerProbe.Result probe)
    {
        if (_secrets.ServerShopId is not null) return;
        if (probe.Problem is not null) return;
        if (string.IsNullOrWhiteSpace(probe.ShopId)) return;

        try { _secrets.SetServerShopId(probe.ShopId); }
        catch (Exception) { /* yozib bo'lmasa ish to'xtamaydi */ }
    }

    private static void OpenExternally(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return;
        if (parsed.Scheme != Uri.UriSchemeHttp
            && parsed.Scheme != Uri.UriSchemeHttps
            && parsed.Scheme != Uri.UriSchemeMailto)
        {
            return;
        }

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
    /// Server ko'tarilishini kutadi va o'zi ulanadi.
    /// </summary>
    /// <remarks>
    /// <para>Kassirdan hech narsa talab qilinmaydi: elektr uzilishidan keyin
    /// ikkala kompyuter birga yonadi va server kassa kechroq tayyor bo'ladi.
    /// Ilova shunchaki kutadi va tayyor bo'lgan zahoti ish ekraniga
    /// o'tadi.</para>
    ///
    /// <para>Oraliq ATAYLAB kichik emas: server ko'tarilishi bir daqiqagacha
    /// cho'zilishi mumkin va har soniyada so'rov yuborish uni yanada
    /// sekinlashtirardi.</para>
    /// </remarks>
    private void RetryUntilServerUp(string serverUrl)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) =>
        {
            if (IsDisposed) { timer.Stop(); timer.Dispose(); return; }

            var probe = await ServerProbe.ProbeAsync(serverUrl, CancellationToken.None);
            if (probe.Problem is not null) return;   // hali tayyor emas — kutamiz

            // Kutish paytida manzil BOSHQA kompyuterga o'tgan bo'lishi
            // mumkin — aynan shu holat routerni qayta yoqqandan keyin yuz
            // beradi. Begona bazaga ulanib ketmaymiz.
            if (ServerProbe.ShopMismatch(_secrets.ServerShopId, probe) is { } wrongShop)
            {
                timer.Stop();
                timer.Dispose();
                FailWithSetup(wrongShop, serverUrl);
                return;
            }

            RememberShop(probe);

            timer.Stop();
            timer.Dispose();

            // Sozlama tugmasi endi keraksiz: ulanish tiklandi.
            if (_setup is not null) { _setup.Hide(); }

            try
            {
                await ShowInterfaceAsync(serverUrl);
                WatchServer(serverUrl);
            }
            catch (Exception ex)
            {
                Fail("Interfeysni ochib bo'lmadi." + Environment.NewLine + Environment.NewLine + ex.Message);
            }
        };
        timer.Start();
        Disposed += (_, _) => timer.Dispose();
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
