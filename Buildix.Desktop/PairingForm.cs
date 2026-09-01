using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Buildix.Desktop;

/// <summary>
/// Do'konni bulutga bog'lash oynasi — o'rnatish kunida bir marta.
///
/// <para><b>Nima uchun kerak.</b> Yangi o'rnatilgan do'kon bazasi BO'SH: na
/// market, na foydalanuvchi. Ya'ni ilova ochiladi, lekin kirish oynasidan
/// nariga o'tib bo'lmaydi. Bog'lanish do'konga o'z xodimlarini olib
/// keladi.</para>
///
/// <para><b>Nega qobiqda, interfeysda emas.</b> Interfeysga kirish uchun
/// foydalanuvchi kerak, foydalanuvchi esa aynan shu qadamdan keyin paydo
/// bo'ladi. Tovuq va tuxum: uzilishning yagona joyi — qobiq.</para>
///
/// <para><b>Ikki yo'l.</b> Asosiysi — do'kon EGASINING login-paroli: u
/// hisobiga allaqachon ega va undan boshqa isbot so'rashning ma'nosi yo'q.
/// Zaxirasi — paneldan olingan bir martalik kod: egasi yo'q bo'lganda yoki
/// paroli esdan chiqqanda qo'llab-quvvatlash shu yo'l bilan yordam
/// beradi.</para>
/// </summary>
public sealed class PairingForm : Form
{
    private readonly LocalSecrets _secrets;

    private readonly TextBox _cloud = new() { Width = 320 };
    private readonly TextBox _username = new() { Width = 220 };
    private readonly TextBox _password = new() { Width = 220, UseSystemPasswordChar = true };
    private readonly TextBox _code = new() { Width = 200, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _name = new() { Width = 220, Text = "Server kassa" };
    // Balandligi ATAYLAB katta: bulutning xato matni bir necha jumla bo'lishi
    // mumkin («allaqachon bog'langan» xabari shunday) va kichik yorliqda u
    // gap o'rtasida qirqilib qolardi — ya'ni kassir aynan nima qilish
    // kerakligini o'qiy olmasdi.
    private readonly Label _result = new() { AutoSize = false, Width = 480, Height = 96 };
    private readonly Button _pair = new() { Text = "Kirish va bog'lash", Width = 170 };
    private readonly LinkLabel _switchMode = new() { AutoSize = true, Text = "Kod bilan bog'lash" };

    /// <summary>
    /// 2-kassa yo'li. Bulutga bog'lash unga UMUMAN kerak emas.
    /// </summary>
    /// <remarks>
    /// <para>Bu oyna yangi o'rnatilgan har bir kompyuterda birinchi bo'lib
    /// chiqadi va faqat bitta yo'lni — bulutga bog'lanishni — ko'rsatardi.
    /// 2-kassada esa u boshi berk ko'cha: do'konga allaqachon kompyuter
    /// bog'langan, ya'ni urinish «allaqachon bog'langan» xatosiga uriladi va
    /// texnikda hech qanday davom yo'li qolmasdi. Sozlash oynasi
    /// (<c>--setup</c>) mavjud edi, lekin uni bilish kerak edi.</para>
    /// </remarks>
    private readonly LinkLabel _asClient = new()
    {
        AutoSize = true,
        Text = "Bu kompyuter — 2-kassa (server kassaga ulanadi)",
    };

    /// <summary>
    /// Foydalanuvchi shu oynadan turib kassani ULANUVCHI qilib sozladi.
    ///
    /// <para>Rol ishga tushishda hal bo'ladi (baza va API ko'tariladimi yoki
    /// yo'q), shuning uchun uni yo'l-yo'lakay almashtirib bo'lmaydi — qobiq
    /// buni ko'rib, qayta ochishni so'raydi.</para>
    /// </summary>
    public bool SwitchedToClient { get; private set; }

    private readonly TableLayoutPanel _loginRows = new();
    private readonly TableLayoutPanel _codeRows = new();

    /// <summary>Kod rejimi yoqilganmi. Sukut bo'yicha — login-parol.</summary>
    private bool _codeMode;

    public PairingForm(LocalSecrets secrets)
    {
        _secrets = secrets;
        _cloud.Text = secrets.MarketUrl;

        Text = "Buildix — do'konni bog'lash";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 470);
        Font = new Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = false,
            Width = 490,
            Height = 76,
            Text = "Bu kompyuter hali bulutga bog'lanmagan, shuning uchun do'kon "
                 + "xodimlari hali yo'q va tizimga kirib bo'lmaydi.\r\n\r\n"
                 + "O'z do'koningizning manzilini yozing va do'kon EGASINING "
                 + "login-paroli bilan kiring. Bu do'konning BIRINCHI (server) "
                 + "kompyuteri uchun.",
        };

        BuildLoginRows();
        BuildCodeRows();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
        };
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(Row("Do'kon manzili:", _cloud), 0, 1);
        layout.Controls.Add(_loginRows, 0, 2);
        layout.Controls.Add(_codeRows, 0, 3);
        // Kassa nomi ikkala yo'lda ham kerak, shuning uchun u almashadigan
        // qismdan TASHQARIDA turadi — rejimni almashtirganda yozilgan nom
        // yo'qolmasin.
        layout.Controls.Add(Row("Kassa nomi:", _name), 0, 4);
        // 2-kassa yo'li xato chiqishidan OLDIN ko'rinadi: aks holda texnik
        // avval bulutga bog'lashga urinib, tushunarsiz xatoga uriladi.
        _asClient.Margin = new Padding(0, 4, 0, 6);
        layout.Controls.Add(_asClient, 0, 5);
        layout.Controls.Add(_result, 0, 6);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        // «Keyinroq» ATAYLAB bor: bog'lanmagan do'kon ham ochilishi kerak.
        // Do'kon allaqachon ishlab turgan bo'lishi mumkin (masalan baza
        // zaxiradan tiklangan) va o'shanda majburiy bog'lash ilovani
        // ochilmaydigan qilib qo'yardi.
        var later = new Button { Text = "Keyinroq", Width = 120, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(later);
        buttons.Controls.Add(_pair);
        _switchMode.Margin = new Padding(0, 9, 18, 0);
        buttons.Controls.Add(_switchMode);
        layout.Controls.Add(buttons, 0, 7);

        Controls.Add(layout);
        AcceptButton = _pair;
        CancelButton = later;

        _switchMode.LinkClicked += (_, _) => SetMode(!_codeMode);
        _asClient.LinkClicked += (_, _) => SetUpAsClient();
        _pair.Click += async (_, _) => await PairAsync();

        SetMode(codeMode: false);
    }

    private void BuildLoginRows()
    {
        _loginRows.ColumnCount = 1;
        _loginRows.AutoSize = true;
        _loginRows.Margin = new Padding(0);
        _loginRows.Controls.Add(Row("Login:", _username));
        _loginRows.Controls.Add(Row("Parol:", _password));
    }

    private void BuildCodeRows()
    {
        _codeRows.ColumnCount = 1;
        _codeRows.AutoSize = true;
        _codeRows.Margin = new Padding(0);
        _codeRows.Controls.Add(Row("Kod:", _code));
    }

    /// <summary>Yorliq + maydon juftligi.</summary>
    private static Control Row(string caption, Control field)
    {
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = caption, Width = 110, Margin = new Padding(0, 6, 0, 0) });
        row.Controls.Add(field);
        return row;
    }

    private void SetMode(bool codeMode)
    {
        _codeMode = codeMode;
        _loginRows.Visible = !codeMode;
        _codeRows.Visible = codeMode;
        _pair.Text = codeMode ? "Kod bilan bog'lash" : "Kirish va bog'lash";
        _switchMode.Text = codeMode ? "Login-parol bilan bog'lash" : "Kod bilan bog'lash";
        Show(Color.DimGray, string.Empty);
        if (codeMode) _code.Focus(); else _username.Focus();
    }

    /// <summary>
    /// Kassani ULANUVCHI qilib sozlaydi — sozlash oynasini ochib.
    /// </summary>
    /// <remarks>
    /// <para>Sozlash oynasi bu ishni allaqachon biladi (rol, manzil,
    /// «Tekshirish», printerlar), shuning uchun bu yerda uni takrorlash
    /// ma'nosiz bo'lardi va ikki nusxa vaqt o'tishi bilan ajralib
    /// ketardi.</para>
    ///
    /// <para>Manzil YOZILGANINI tekshiramiz: texnik oynani ochib, hech narsa
    /// o'zgartirmasdan yopgan bo'lishi mumkin va o'shanda bog'lash oynasi
    /// joyida qolishi kerak.</para>
    /// </remarks>
    private void SetUpAsClient()
    {
        using var setup = new SetupForm(_secrets);
        if (setup.ShowDialog(this) != DialogResult.OK) return;
        if (_secrets.ServerUrl is null)
        {
            Show(Color.Firebrick,
                "Sozlamada «Bu kompyuter server kassaga ULANADI» tanlanmagan — "
                + "kassa hamon server rejimida.");
            return;
        }

        SwitchedToClient = true;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private async Task PairAsync()
    {
        var address = MarketAddress.Parse(_cloud.Text);
        if (address is null)
        {
            Show(Color.Firebrick, "Do'kon manzilini kiriting. Namuna: buildix.uz/taxtapul");
            return;
        }

        // Do'kon belgisisiz bulut foydalanuvchini BARCHA do'konlar ichidan
        // qidiradi va bir xil login ikki do'konda uchrasa qaysi biri
        // tanlanishi aniqlanmagan bo'ladi — kassa begona do'konga bog'lanib
        // ketishi mumkin. Kod bilan bog'lashda bu xavf yo'q: kod o'zi
        // do'konga tegishli.
        if (!_codeMode && address.Subdomain is null)
        {
            Show(Color.Firebrick,
                "Manzilda do'kon nomi yo'q. To'liq manzilni yozing — namuna: buildix.uz/taxtapul");
            return;
        }

        var url = address.CloudUrl;
        var terminalName = _name.Text.Trim();

        // Har rejimning o'z manzili va o'z tanasi — qolgan hammasi bir xil.
        string path;
        object body;
        if (_codeMode)
        {
            var code = _code.Text.Trim();
            if (string.IsNullOrWhiteSpace(code)) { Show(Color.Firebrick, "Kodni kiriting."); return; }
            path = "/api/pairing/redeem";
            body = new { code, terminalName };
        }
        else
        {
            var username = _username.Text.Trim();
            var password = _password.Text;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                Show(Color.Firebrick, "Login va parolni kiriting.");
                return;
            }
            path = "/api/pairing/activate";
            // Do'kon belgisi AYNAN shu yerda hal qiluvchi: usiz bulut
            // loginni barcha do'konlar ichidan qidiradi.
            body = new { username, password, subdomain = address.Subdomain, terminalName };
        }

        _pair.Enabled = false;
        Show(Color.DimGray, "Bulutga ulanmoqda…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var response = await http.PostAsJsonAsync(url + path, body);

            if (!response.IsSuccessStatusCode)
            {
                // Bulut xabari o'zbekcha va aniq — uni yashirmaymiz.
                var problem = await ReadMessageAsync(response);
                Show(Color.Firebrick, problem);
                return;
            }

            var paired = await response.Content.ReadFromJsonAsync<PairedResponse>();
            if (paired is null || string.IsNullOrWhiteSpace(paired.Key))
            {
                Show(Color.Firebrick, "Bulut kutilmagan javob qaytardi.");
                return;
            }

            _secrets.SetCloudPairing(url, paired.Key, address.Subdomain);
            Show(Color.SeaGreen, $"«{paired.MarketName}» do'koniga bog'landi.");

            // Ma'lumot API ishga tushganda o'zi tortiladi, shuning uchun
            // shu yerda kutish shart emas.
            await Task.Delay(900);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (HttpRequestException ex)
        {
            Show(Color.Firebrick, "Bulutga ulanib bo'lmadi. Internetni tekshiring.\r\n" + ex.Message);
        }
        catch (TaskCanceledException)
        {
            Show(Color.Firebrick, "Bulut javob bermadi. Keyinroq urinib ko'ring.");
        }
        finally
        {
            _pair.Enabled = true;
        }
    }

    /// <summary>Bulutning xato matnini oladi; bo'lmasa umumiy xabar.</summary>
    private static async Task<string> ReadMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ProblemBody>();
            if (!string.IsNullOrWhiteSpace(body?.Message)) return body!.Message!;
        }
        catch (Exception) { /* JSON emas — quyidagi umumiy xabar */ }

        return response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            ? "Juda ko'p urinish bo'ldi. Bir oz kutib, qaytadan urining."
            : $"Bog'lanmadi (kod {(int)response.StatusCode}).";
    }

    private void Show(Color color, string text)
    {
        _result.ForeColor = color;
        _result.Text = text;
    }

    private sealed record PairedResponse(
        [property: JsonPropertyName("marketId")] int MarketId,
        [property: JsonPropertyName("marketName")] string MarketName,
        [property: JsonPropertyName("key")] string Key);

    private sealed record ProblemBody(
        [property: JsonPropertyName("message")] string? Message);
}
