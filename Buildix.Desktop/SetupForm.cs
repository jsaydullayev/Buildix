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
    private readonly TextBox _address = new() { Width = 260 };
    private readonly Label _result = new() { AutoSize = false, Width = 470, Height = 40 };
    private readonly Button _test = new() { Text = "Tekshirish", Width = 110 };
    private readonly Button _firewall = new() { Text = "Tarmoq ruxsatini ochish", Width = 190 };
    private readonly Button _save = new() { Text = "Saqlash", Width = 110, DialogResult = DialogResult.OK };

    public SetupForm(LocalSecrets secrets)
    {
        _secrets = secrets;

        Text = "Buildix — kassa sozlamasi";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 360);
        Font = new Font("Segoe UI", 9.75F);

        var current = _secrets.ServerUrl;
        _server.Checked = current is null;
        _client.Checked = current is not null;
        _lan.Checked = _secrets.AllowLan;
        _address.Text = current ?? $"http://192.168.1.10:{DefaultPort}";

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 3,
            RowCount = 9,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(_server, 0, 0);
        layout.SetColumnSpan(_server, 3);

        var serverHint = new Label
        {
            Text = "Bir do'konda faqat BITTA server bo'ladi. Boshqa kassalar shunga ulanadi.",
            AutoSize = false, Width = 460, Height = 20, ForeColor = SystemColors.GrayText,
            Margin = new Padding(20, 0, 0, 8),
        };
        layout.Controls.Add(serverHint, 0, 1);
        layout.SetColumnSpan(serverHint, 3);

        layout.Controls.Add(_lan, 0, 2);
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
        layout.Controls.Add(addresses, 0, 3);
        layout.SetColumnSpan(addresses, 3);

        layout.Controls.Add(_firewall, 0, 4);
        layout.SetColumnSpan(_firewall, 3);

        layout.Controls.Add(_client, 0, 5);
        layout.SetColumnSpan(_client, 3);

        layout.Controls.Add(new Label { Text = "Manzil:", AutoSize = true, Margin = new Padding(20, 6, 6, 0) }, 0, 6);
        layout.Controls.Add(_address, 1, 6);
        layout.Controls.Add(_test, 2, 6);

        layout.Controls.Add(_result, 0, 7);
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
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 3);

        Controls.Add(layout);
        AcceptButton = _save;
        CancelButton = cancel;

        _server.CheckedChanged += (_, _) => SyncEnabled();
        _test.Click += async (_, _) => await TestAsync();
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

    private void Persist()
    {
        var isClient = _client.Checked;
        _secrets.SetServerUrl(isClient ? Normalize(_address.Text) ?? _address.Text : null);
        // Ulanuvchi kassa hech qachon tarmoqqa ochilmaydi: unda API umuman
        // ishga tushmaydi va yoqilgan bayroq faqat chalkashtirardi.
        _secrets.SetAllowLan(!isClient && _lan.Checked);
    }

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
            var error = await ServerProbe.CheckAsync(address, CancellationToken.None);
            if (error is null)
                Show(Color.SeaGreen, "Server javob berdi. Saqlash mumkin.");
            else
                Show(Color.Firebrick, error);
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
                Show(Color.SeaGreen, $"{DefaultPort}-port do'kon tarmog'i uchun ochildi.");
            else
                Show(Color.Firebrick, "Qoida qo'shilmadi. Administrator huquqi kerak.");
        }
        catch (Exception ex)
        {
            // Eng ko'p uchraydigani — foydalanuvchi UAC oynasida «Yo'q» bosgani.
            Show(Color.Firebrick, "Ruxsat berilmadi: " + ex.Message);
        }
    }
}
