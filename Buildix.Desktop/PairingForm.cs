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
    private readonly Label _result = new() { AutoSize = false, Width = 470, Height = 52 };
    private readonly Button _pair = new() { Text = "Kirish va bog'lash", Width = 170 };
    private readonly LinkLabel _switchMode = new() { AutoSize = true, Text = "Kod bilan bog'lash" };

    private readonly TableLayoutPanel _loginRows = new();
    private readonly TableLayoutPanel _codeRows = new();

    /// <summary>Kod rejimi yoqilganmi. Sukut bo'yicha — login-parol.</summary>
    private bool _codeMode;

    public PairingForm(LocalSecrets secrets)
    {
        _secrets = secrets;
        _cloud.Text = secrets.CloudUrl ?? "https://buildix.uz";

        Text = "Buildix — do'konni bog'lash";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(540, 400);
        Font = new Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = false,
            Width = 490,
            Height = 60,
            Text = "Bu kompyuter hali bulutga bog'lanmagan, shuning uchun do'kon "
                 + "xodimlari hali yo'q va tizimga kirib bo'lmaydi.\r\n\r\n"
                 + "Do'kon EGASINING login va paroli bilan kiring.",
        };

        BuildLoginRows();
        BuildCodeRows();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7,
            AutoSize = true,
        };
        layout.Controls.Add(intro, 0, 0);
        layout.Controls.Add(Row("Bulut manzili:", _cloud), 0, 1);
        layout.Controls.Add(_loginRows, 0, 2);
        layout.Controls.Add(_codeRows, 0, 3);
        // Kassa nomi ikkala yo'lda ham kerak, shuning uchun u almashadigan
        // qismdan TASHQARIDA turadi — rejimni almashtirganda yozilgan nom
        // yo'qolmasin.
        layout.Controls.Add(Row("Kassa nomi:", _name), 0, 4);
        layout.Controls.Add(_result, 0, 5);

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
        layout.Controls.Add(buttons, 0, 6);

        Controls.Add(layout);
        AcceptButton = _pair;
        CancelButton = later;

        _switchMode.LinkClicked += (_, _) => SetMode(!_codeMode);
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

    private async Task PairAsync()
    {
        var url = _cloud.Text.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            Show(Color.Firebrick, "Bulut manzilini kiriting.");
            return;
        }

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
            body = new { username, password, subdomain = (string?)null, terminalName };
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

            _secrets.SetCloudPairing(url, paired.Key);
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
