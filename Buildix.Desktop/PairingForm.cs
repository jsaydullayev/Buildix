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
/// </summary>
public sealed class PairingForm : Form
{
    private readonly LocalSecrets _secrets;

    private readonly TextBox _cloud = new() { Width = 320 };
    private readonly TextBox _code = new() { Width = 200, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _name = new() { Width = 200, Text = "Server kassa" };
    private readonly Label _result = new() { AutoSize = false, Width = 460, Height = 46 };
    private readonly Button _pair = new() { Text = "Bog'lash", Width = 120 };

    public PairingForm(LocalSecrets secrets)
    {
        _secrets = secrets;
        _cloud.Text = secrets.CloudUrl ?? "https://buildix.uz";

        Text = "Buildix — do'konni bog'lash";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 330);
        Font = new Font("Segoe UI", 9.75F);

        var intro = new Label
        {
            AutoSize = false,
            Width = 470,
            Height = 62,
            Text = "Bu kompyuter hali bulutga bog'lanmagan, shuning uchun do'kon "
                 + "xodimlari hali yo'q va tizimga kirib bo'lmaydi.\r\n\r\n"
                 + "Buildix panelidan olingan bir martalik kodni kiriting.",
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 2);

        layout.Controls.Add(Caption("Bulut manzili:"), 0, 1);
        layout.Controls.Add(_cloud, 1, 1);
        layout.Controls.Add(Caption("Kod:"), 0, 2);
        layout.Controls.Add(_code, 1, 2);
        layout.Controls.Add(Caption("Kassa nomi:"), 0, 3);
        layout.Controls.Add(_name, 1, 3);

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

        layout.Controls.Add(_result, 0, 4);
        layout.SetColumnSpan(_result, 2);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = _pair;
        CancelButton = later;

        _pair.Click += async (_, _) => await PairAsync();
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 6, 0) };

    private async Task PairAsync()
    {
        var url = _cloud.Text.Trim().TrimEnd('/');
        var code = _code.Text.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(code))
        {
            Show(Color.Firebrick, "Bulut manzili va kodni kiriting.");
            return;
        }

        _pair.Enabled = false;
        Show(Color.DimGray, "Bulutga ulanmoqda…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var response = await http.PostAsJsonAsync(
                $"{url}/api/pairing/redeem",
                new { code, terminalName = _name.Text.Trim() });

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
