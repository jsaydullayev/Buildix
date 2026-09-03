using System.Diagnostics;

namespace Buildix.Desktop;

/// <summary>
/// Kassaning tarmoqdagi o'rnini belgilaydi: bazani o'zi ko'taradimi yoki
/// server kassaga ulanadimi.
///
/// <para><b>Nega alohida oyna, JSON emas.</b> Sozlama
/// <c>%ProgramData%\Buildix\desktop.json</c> da turadi va o'sha faylda BAZA
/// PAROLI ham bor. Texnikni uni Bloknotda ochishga majburlash — parolni
/// tasodifan buzib qo'yishning eng oson yo'li, undan keyin esa bazani umuman
/// ochib bo'lmaydi.</para>
///
/// <para><b>Nega manzil darhol tekshiriladi.</b> Noto'g'ri yozilgan manzil
/// faqat ilova qayta ochilganda bilinardi — texnik allaqachon do'kondan
/// chiqib ketgan bo'lardi. Bu yerda «Tekshirish» tugmasi serverga haqiqiy
/// so'rov yuboradi va javobni shu yerda ko'rsatadi.</para>
/// </summary>
public sealed class SetupForm : Form
{
    private const int DefaultPort = 5088;

    private readonly LocalSecrets _secrets;
    private readonly RadioButton _server = new() { Text = "Bu kompyuter — SERVER (baza shu yerda turadi)", AutoSize = true };
    private readonly RadioButton _client = new() { Text = "Bu kompyuter server kassaga ULANADI", AutoSize = true };
    private readonly CheckBox _lan = new()
    {
        Text = "Boshqa kassalar shu kompyuterga ulanadi (tarmoqqa ochish)",
        AutoSize = true,
        Margin = new Padding(20, 0, 0, 0),
    };
    /// <summary>
    /// Shu kassaning qisqa belgisi — «A», «B», «1».
    /// </summary>
    /// <remarks>
    /// <para>Ikki kassali do'konda chek qaysi birida urilgani hech qayerda
    /// ko'rinmasdi: sotuvchi «kim sotgan» ni aytadi, «qayerda» ni emas —
    /// bitta kassir kun davomida ikkala kassada ham ishlashi mumkin.</para>
    ///
    /// <para>Har kompyuterda ALOHIDA qo'yiladi va shu kompyuterdan
    /// yuborilgan har bir so'rov bilan ketadi. Bitta kassali do'konda
    /// bo'sh qoldirilsa bo'ladi.</para>
    /// </remarks>
    private readonly TextBox _register = new()
    {
        Width = 60,
        MaxLength = 4,
        CharacterCasing = CharacterCasing.Upper,
    };

    private readonly TextBox _address = new() { Width = 260 };
    private readonly TextBox _feed = new() { Width = 260 };
    private readonly Label _result = new() { AutoSize = false, Width = 470, Height = 40 };
    private readonly Button _test = new() { Text = "Tekshirish", Width = 110 };
    private readonly Button _firewall = new() { Text = "Tarmoq ruxsatini ochish", Width = 190 };
    /// <summary>
    /// Yorliq printeri — do'konda odatda ikkita printer bo'ladi (chek va
    /// yorliq). Bir marta tanlanadi va yorliq shundan keyin chop etish
    /// oynasisiz, aynan kerakli o'lchamda chiqadi. Bo'sh qoldirilsa
    /// avvalgidek oyna ochiladi.
    /// </summary>
    private readonly ComboBox _printer = new()
    {
        Width = 260,
        DropDownStyle = ComboBoxStyle.DropDownList,
    };

    /// <summary>
    /// Kassa cheki printeri — yorliqnikidan ALOHIDA.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari chek umuman qobiq orqali bosilmasdi: u brauzerning chop
    /// etish yo'liga tushar, u yerda esa sukut bo'yicha A4 printer va
    /// «sahifaga moslash» turardi. 80 mm chek qog'ozga sig'masdi va har bir
    /// harf alohida qatorga tushib, chek yarim metrga cho'zilardi.</para>
    ///
    /// <para><b>Nega yozsa ham bo'ladi.</b> Ro'yxat Windows'ga QO'SHILGAN
    /// printerlarni ko'rsatadi. Tarmoq printeri esa ko'p do'konda
    /// qo'shilmagan — u shunchaki router orqali ulangan quti. Shu maydonga
    /// uning IP manzilini yozish kifoya: chek unga 9100-port orqali
    /// to'g'ridan-to'g'ri boradi.</para>
    /// </remarks>
    private readonly ComboBox _receiptPrinter = new()
    {
        Width = 260,
        // Yozib ham bo'ladi: tarmoq printeri ro'yxatda bo'lmaydi.
        DropDownStyle = ComboBoxStyle.DropDown,
    };

    /// <summary>
    /// Sozlamani AYNAN shu yerda, texnik do'kondan chiqib ketmasdan
    /// tekshirish uchun. Ilgari yagona yo'l haqiqiy savdo qilish edi.
    /// </summary>
    private readonly Button _testReceipt = new() { Text = "Sinov cheki", Width = 110 };

    private readonly Button _save = new() { Text = "Saqlash", Width = 110, DialogResult = DialogResult.OK };

    public SetupForm(LocalSecrets secrets)
    {
        _secrets = secrets;

        Text = "Buildix — kassa sozlamasi";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 520);
        Font = new Font("Segoe UI", 9.75F);

        var current = _secrets.ServerUrl;
        // Saqlangan belgi AYNAN shu manzil uchun olingan. Oyna «Tekshirish»siz
        // yopilsa, manzil o'zgarmagani shundan bilinadi.
        _probedAddress = current;
        _server.Checked = current is null;
        _client.Checked = current is not null;
        _lan.Checked = _secrets.AllowLan;
        _address.Text = current ?? $"http://192.168.1.10:{DefaultPort}";

        // Windows'dagi printerlar. Ro'yxat bo'sh bo'lishi mumkin (printer
        // ulanmagan) — o'shanda faqat «tanlanmagan» qatori qoladi va
        // yorliq avvalgidek oyna orqali bosiladi.
        _printer.Items.Add(NoPrinter);
        foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            _printer.Items.Add(name);
        _feed.Text = _secrets.UpdateFeedUrl ?? "";
        _register.Text = _secrets.RegisterCode ?? "";
        _receiptPrinter.Items.Add(NoPrinter);
        foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            _receiptPrinter.Items.Add(name);
        // Saqlangan qiymat IP manzil ham bo'lishi mumkin — u ro'yxatda
        // yo'q, shuning uchun matn sifatida qo'yiladi.
        _receiptPrinter.Text = _secrets.ReceiptPrinter ?? NoPrinter;

        _printer.SelectedItem = _secrets.LabelPrinter is { } saved && _printer.Items.Contains(saved)
            ? saved
            : NoPrinter;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 3,
            RowCount = 16,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Kassa belgisi — rol tanlashdan OLDIN, chunki u ikkala rejimga ham
        // tegishli: server kassaning ham, ulanuvchi kassaning ham o'z
        // belgisi bo'ladi.
        var registerRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        registerRow.Controls.Add(new Label
        {
            Text = "Kassa belgisi:", AutoSize = true, Margin = new Padding(0, 6, 6, 0),
        });
        registerRow.Controls.Add(_register);
        registerRow.Controls.Add(new Label
        {
            Text = "«A», «B» — chekda va sotuvlar ro'yxatida ko'rinadi. Bitta kassali do'konda shart emas.",
            AutoSize = false, Width = 380, Height = 32,
            ForeColor = SystemColors.GrayText, Margin = new Padding(8, 4, 0, 0),
        });
        layout.Controls.Add(registerRow, 0, 0);
        layout.SetColumnSpan(registerRow, 3);

        layout.Controls.Add(_server, 0, 1);
        layout.SetColumnSpan(_server, 3);

        var serverHint = new Label
        {
            Text = "Bir do'konda faqat BITTA server bo'ladi. Boshqa kassalar shunga ulanadi.",
            AutoSize = false, Width = 460, Height = 20, ForeColor = SystemColors.GrayText,
            Margin = new Padding(20, 0, 0, 8),
        };
        layout.Controls.Add(serverHint, 0, 2);
        layout.SetColumnSpan(serverHint, 3);

        layout.Controls.Add(_lan, 0, 3);
        layout.SetColumnSpan(_lan, 3);

        // Boshqa kassalarda AYNAN shu manzil yoziladi. Uni ko'rsatmaslik
        // texnikni ipconfig ga yuborardi va u yerdan noto'g'ri (masalan
        // virtual mashina yoki VPN) manzilni ko'chirib olish oson.
        var addresses = new Label
        {
            Text = "Shu kompyuterning manzili: " + LocalAddresses(),
            AutoSize = false, Width = 470, Height = 20,
            Margin = new Padding(20, 2, 0, 8),
        };
        layout.Controls.Add(addresses, 0, 4);
        layout.SetColumnSpan(addresses, 3);

        layout.Controls.Add(_firewall, 0, 5);
        layout.SetColumnSpan(_firewall, 3);

        layout.Controls.Add(_client, 0, 6);
        layout.SetColumnSpan(_client, 3);

        layout.Controls.Add(new Label { Text = "Manzil:", AutoSize = true, Margin = new Padding(20, 6, 6, 0) }, 0, 7);
        layout.Controls.Add(_address, 1, 7);
        layout.Controls.Add(_test, 2, 7);

        layout.Controls.Add(new Label
        {
            Text = "Yorliq printeri:", AutoSize = true, Margin = new Padding(0, 12, 6, 0),
        }, 0, 8);
        layout.Controls.Add(_printer, 1, 8);
        var printerHint = new Label
        {
            AutoSize = false, Width = 470, Height = 30,
            ForeColor = SystemColors.GrayText,
            Text = "Tanlansa — yorliq to'g'ridan-to'g'ri shu printerga, aniq o'lchamda chiqadi. "
                 + "Bo'sh qoldirilsa har safar chop etish oynasi ochiladi.",
        };
        layout.Controls.Add(printerHint, 0, 9);
        layout.SetColumnSpan(printerHint, 3);

        layout.Controls.Add(new Label
        {
            Text = "Chek printeri:", AutoSize = true, Margin = new Padding(0, 12, 6, 0),
        }, 0, 10);
        layout.Controls.Add(_receiptPrinter, 1, 10);
        layout.Controls.Add(_testReceipt, 2, 10);
        var receiptHint = new Label
        {
            AutoSize = false, Width = 470, Height = 30,
            ForeColor = SystemColors.GrayText,
            Text = "Rulonli printer: ro'yxatdan tanlang yoki tarmoq printerining IP "
                 + "manzilini yozing (masalan 192.168.1.50). «Sinov cheki» darhol tekshiradi.",
        };
        layout.Controls.Add(receiptHint, 0, 11);
        layout.SetColumnSpan(receiptHint, 3);

        // ── Yangilanish manzili ────────────────────────────────────────────
        // Ilgari uni faqat desktop.json ni qo'lda tahrirlab qo'yish mumkin
        // edi. Texnik buni unutsa, do'kon HECH QACHON yangilanmasdi va buni
        // bilishning yo'li yo'q edi: xato chiqmaydi, sarlavha o'zgarmaydi.
        layout.Controls.Add(new Label
        {
            Text = "Yangilanish:", AutoSize = true, Margin = new Padding(0, 6, 6, 0),
        }, 0, 12);
        layout.Controls.Add(_feed, 1, 12);

        var feedHint = new Label
        {
            AutoSize = false, Height = 32, Width = 520,
            ForeColor = SystemColors.GrayText,
            Text = "Yangi versiyalar shu manzildan olinadi. Bo'sh qoldirilsa ilova "
                 + "yangilanmaydi — bu ataylab: sinov do'konini alohida kanalga "
                 + "yo'naltirish mumkin.",
        };
        layout.Controls.Add(feedHint, 0, 13);
        layout.SetColumnSpan(feedHint, 3);

        layout.Controls.Add(_result, 0, 14);
        layout.SetColumnSpan(_result, 3);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        var cancel = new Button { Text = "Bekor qilish", Width = 110, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 15);
        layout.SetColumnSpan(buttons, 3);

        Controls.Add(layout);
        AcceptButton = _save;
        CancelButton = cancel;

        _server.CheckedChanged += (_, _) => SyncEnabled();
        _test.Click += async (_, _) => await TestAsync();
        _testReceipt.Click += async (_, _) => await TestReceiptAsync();
        _firewall.Click += (_, _) => OpenFirewall();
        _save.Click += (_, _) => Persist();
        SyncEnabled();
    }

    private void SyncEnabled()
    {
        var isClient = _client.Checked;
        _address.Enabled = isClient;
        _test.Enabled = isClient;
        _lan.Enabled = !isClient;
        _firewall.Enabled = !isClient;
    }

    /// <summary>
    /// «Tekshirish» paytida javob bergan do'konning belgisi.
    /// </summary>
    /// <remarks>
    /// Saqlanadi va har ochilishda solishtiriladi: manzil boshqa
    /// kompyuterga o'tib qolsa, kassa begona bazaga ulanmaydi.
    /// Tekshirilmagan bo'lsa <c>null</c> — o'shanda belgi birinchi
    /// muvaffaqiyatli ulanishda yoziladi, ish esa to'xtamaydi.
    /// </remarks>
    private string? _probedShopId;

    /// <summary>
    /// <see cref="_probedShopId"/> QAYSI manzil uchun olingani.
    ///
    /// <para>Belgi manzilga bog'liq: server kompyuteri almashtirilsa yoki
    /// qayta o'rnatilsa, uning do'kon belgisi ham yangi bo'ladi. Manzilni
    /// tekshirmasdan almashtirgan texnik eski belgini yangi manzilga
    /// yopishtirar va kassa keyingi ishga tushishda «boshqa do'kon» deb
    /// bloklanardi.</para>
    /// </summary>
    private string? _probedAddress;

    private void Persist()
    {
        var isClient = _client.Checked;
        var normalized = Normalize(_address.Text);
        _secrets.SetServerUrl(isClient ? normalized ?? _address.Text : null);
        // Server rejimida bu belgi keraksiz — u faqat ULANUVCHI kassaga
        // tegishli. Eski qiymat qolib ketsa, rol almashtirilganda noto'g'ri
        // solishtirishga sabab bo'lardi.
        //
        // Ulanuvchi rejimida esa MAVJUD belgi saqlanadi: texnik oynani
        // «Tekshirish» bosmasdan yopsa (masalan faqat printerni almashtirdi),
        // `_probedShopId` null bo'lar va belgi jimgina o'chib ketardi —
        // himoya esa aynan o'sha belgiga tayanadi.
        //
        // Lekin faqat MANZIL O'ZGARMAGANDA. Aks holda eski serverning belgisi
        // yangi manzilga yopishar va kassa keyingi ishga tushishda «boshqa
        // do'kon» deb bloklanardi — chiqish yo'li esa faqat sozlamalar
        // faylini qo'lda tahrirlash bo'lardi. Belgi tashlab yuborilsa,
        // MainForm uni birinchi muvaffaqiyatli ulanishda qaytadan yozadi.
        var sameAddress = string.Equals(
            normalized, _probedAddress, StringComparison.OrdinalIgnoreCase);
        _secrets.SetServerShopId(isClient
            ? _probedShopId ?? (sameAddress ? _secrets.ServerShopId : null)
            : null);
        // Ulanuvchi kassa hech qachon tarmoqqa ochilmaydi: unda API umuman
        // ishga tushmaydi va yoqilgan bayroq faqat chalkashtirardi.
        _secrets.SetAllowLan(!isClient && _lan.Checked);

        var printer = _printer.SelectedItem as string;
        _secrets.SetLabelPrinter(printer == NoPrinter ? null : printer);

        _secrets.SetReceiptPrinter(ReceiptTarget());
        _secrets.SetUpdateFeedUrl(_feed.Text);
        _secrets.SetRegisterCode(_register.Text);
    }

    /// <summary>
    /// Maydondagi qiymat — printer nomi yoki IP manzil; «tanlanmagan» bo'lsa
    /// <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>SelectedItem</c> EMAS: maydonga yozish mumkin va tarmoq printeri
    /// ro'yxatda umuman bo'lmaydi — o'shanda tanlangan element <c>null</c>
    /// bo'lib, texnik yozgan IP jimgina yo'qolardi.
    /// </remarks>
    private string? ReceiptTarget()
    {
        var text = _receiptPrinter.Text.Trim();
        return text.Length == 0 || text == NoPrinter ? null : text;
    }

    /// <summary>
    /// Tanlangan printerga qisqa sinov cheki yuboradi.
    /// </summary>
    /// <remarks>
    /// Saqlanmagan qiymat bilan ishlaydi — texnik avval tekshirib, keyin
    /// saqlaydi. Aks holda noto'g'ri manzil saqlanib qolar va uni faqat
    /// keyingi savdoda bilish mumkin bo'lardi.
    /// </remarks>
    private async Task TestReceiptAsync()
    {
        var target = ReceiptTarget();
        if (target is null)
        {
            Show(Color.Firebrick, "Avval printerni tanlang yoki IP manzilini yozing.");
            return;
        }

        _testReceipt.Enabled = false;
        Show(SystemColors.GrayText, "Chek yuborilmoqda…");
        try
        {
            var problem = await ReceiptOutput.SendAsync(
                target, ReceiptOutput.TestSlip(), CancellationToken.None);

            if (problem is null)
                Show(Color.SeaGreen, "Yuborildi. Qog'oz chiqqanini tekshiring.");
            else
                Show(Color.Firebrick, problem);
        }
        finally
        {
            _testReceipt.Enabled = true;
        }
    }

    /// <summary>Ro'yxatdagi «tanlanmagan» qatori.</summary>
    private const string NoPrinter = "— tanlanmagan (chop etish oynasi ochiladi)";

    /// <summary>
    /// Shu kompyuterning lokal tarmoqdagi manzil(lar)i — boshqa kassalarda
    /// AYNAN shu yoziladi.
    ///
    /// <para><b>Nega ro'yxat filtrlanadi.</b> Oddiy do'kon kompyuterida ham
    /// bir nechta «tarmoq adapteri» bo'ladi: Hyper-V virtual kalitlari, VPN
    /// tunnellari, telefon orqali ulanish. Hammasini ko'rsatish texnikni
    /// noto'g'ri manzilni ko'chirishga undaydi va u boshqa kassada nima uchun
    /// ishlamayotganini tushunmaydi.</para>
    ///
    /// <para><b>Ikki belgi birga ishlatiladi.</b> Shlyuz (gateway) bo'lishi —
    /// virtual kalitlarni chiqarib tashlaydi, ularda shlyuz yo'q. Nomdagi
    /// kalit so'zlar esa VPN'ni oladi, chunki unda shlyuz BOR. Bu aniq
    /// qoida emas, taxmin: shubha bo'lsa bir nechtasi ko'rsatiladi va
    /// texnik o'zi tanlaydi.</para>
    /// </summary>
    private static string LocalAddresses()
    {
        // Do'kon tarmog'i emasligi deyarli aniq bo'lgan adapterlar.
        string[] virtualNames =
            ["virtual", "hyper-v", "vpn", "vmware", "virtualbox", "tap-", "tunnel",
             "loopback", "tailscale", "wireguard", "radmin", "docker"];

        try
        {
            var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType
                    is System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                    or System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                .Where(n => !virtualNames.Any(v =>
                    n.Description.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                    n.Name.Contains(v, StringComparison.OrdinalIgnoreCase)))
                .Select(n => n.GetIPProperties())
                // Shlyuzsiz adapter — deyarli har doim virtual kalit.
                .Where(p => p.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !g.Address.Equals(System.Net.IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => $"http://{a}:{DefaultPort}")
                .Distinct()
                .ToList();

            return addresses.Count > 0 ? string.Join("   ", addresses) : "aniqlanmadi";
        }
        catch (Exception)
        {
            // Manzilni ko'rsata olmaslik sozlashni to'xtatmasligi kerak —
            // texnik uni ipconfig dan ham topa oladi.
            return "aniqlanmadi";
        }
    }

    private async Task TestAsync()
    {
        var address = Normalize(_address.Text);
        if (address is null)
        {
            Show(Color.Firebrick, "Manzil noto'g'ri. Namuna: http://192.168.1.10:5088");
            return;
        }

        _address.Text = address;
        _test.Enabled = false;
        Show(SystemColors.GrayText, "Tekshirilmoqda…");
        try
        {
            var probe = await ServerProbe.ProbeAsync(address, CancellationToken.None);
            if (probe.Problem is not null)
            {
                _probedShopId = null;
                _probedAddress = null;
                Show(Color.Firebrick, probe.Problem);
                return;
            }

            // Do'kon NOMI ko'rsatiladi. Bitta tarmoqda ikkita Buildix
            // bo'lishi mumkin (bozordagi qo'shni do'kon, xato sozlangan
            // ikkinchi server) va manzilning o'zi qaysi do'kon ekanini
            // aytmaydi — texnik buni faqat savdo boshlangandan keyin,
            // begona qoldiqlarni ko'rib bilardi.
            _probedShopId = probe.ShopId;
            _probedAddress = address;
            Show(Color.SeaGreen, string.IsNullOrWhiteSpace(probe.ShopName)
                ? "Server javob berdi. Saqlash mumkin."
                : $"Server javob berdi: «{probe.ShopName}». Do'kon to'g'ri bo'lsa saqlang.");
        }
        finally
        {
            _test.Enabled = _client.Checked;
        }
    }

    private void Show(Color color, string text)
    {
        _result.ForeColor = color;
        _result.Text = text;
        _result.Refresh();
    }

    /// <summary>
    /// Foydalanuvchi kiritgan matnni manzilga aylantiradi. Port yozilmasa
    /// standarti qo'yiladi: texnik odatda faqat IP ni biladi va portsiz manzil
    /// 80-portga urinib, tushunarsiz xato berardi.
    /// </summary>
    private static string? Normalize(string raw)
    {
        var text = raw.Trim().TrimEnd('/');
        if (text.Length == 0) return null;
        if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.IsDefaultPort && uri.Scheme == Uri.UriSchemeHttp)
            text = $"http://{uri.Host}:{DefaultPort}";
        return text;
    }

    /// <summary>
    /// Windows brandmauerida kiruvchi portni ochadi.
    ///
    /// <para><b>Nega o'rnatuvchi qilmaydi.</b> Buildix foydalanuvchi papkasiga,
    /// administrator huquqisiz o'rnatiladi — brandmauer qoidasini esa faqat
    /// administrator qo'sha oladi. Shuning uchun bu alohida, ataylab bosiladigan
    /// qadam: UAC so'raydi va texnik nima uchun ekanini biladi.</para>
    ///
    /// <para>Qoida faqat XUSUSIY tarmoq profiliga (do'kon tarmog'i) qo'yiladi.
    /// Ommaviy Wi-Fi ga ulanganda port ochilmaydi.</para>
    /// </summary>
    private void OpenFirewall()
    {
        var arguments =
            "advfirewall firewall add rule name=\"Buildix kassa\" " +
            $"dir=in action=allow protocol=TCP localport={DefaultPort} profile=private";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = true,   // UAC so'rovi uchun shart
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit();

            if (process is { ExitCode: 0 })
            {
                // Qoida faqat XUSUSIY profil uchun. Windows do'kon tarmog'ini
                // «Ommaviy» deb belgilagan bo'lsa u umuman ishlamaydi va
                // 2-kassa ulanolmaydi — tashqaridan bu tarmoq nosozligiga
                // o'xshab ko'rinadi va uni do'konda topish juda qiyin.
                Show(Color.SeaGreen,
                    $"{DefaultPort}-port ochildi. 2-kassa ulanolmasa — Windows'da "
                    + "do'kon tarmog'i «Частная» (xususiy) deb belgilanganini tekshiring.");
            }
            else
            {
                Show(Color.Firebrick, "Qoida qo'shilmadi. Administrator huquqi kerak.");
            }
        }
        catch (Exception ex)
        {
            // Eng ko'p uchraydigani — foydalanuvchi UAC oynasida «Yo'q» bosgani.
            Show(Color.Firebrick, "Ruxsat berilmadi: " + ex.Message);
        }
    }
}
