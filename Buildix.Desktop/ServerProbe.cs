using System.Net.Http;
using System.Text.Json;

namespace Buildix.Desktop;

/// <summary>
/// Server kassa javob berayotganini tekshiradi.
///
/// <para><b>Nega xato matnlari shu yerda.</b> Kassir tarmoq xatolarini
/// o'qimaydi va o'qisa ham «No connection could be made because the target
/// machine actively refused it» unga hech narsa aytmaydi. Har bir holat shu
/// yerda BIR MARTA tarjima qilinadi va sozlash oynasida ham, ish vaqtidagi
/// uzilishda ham bir xil matn chiqadi.</para>
///
/// <para><b>Nega /health.</b> U yagona autentifikatsiyasiz uchraydigan yo'l
/// va bazaga ham tegib ko'radi — ya'ni «server ko'tarilgan, lekin bazasi
/// yiqilgan» holatni ham ushlaydi. Javobda do'konning kimligi ham keladi.</para>
/// </summary>
public static class ServerProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>
    /// Tekshiruv natijasi.
    /// </summary>
    /// <param name="Problem">Hammasi joyida bo'lsa <c>null</c>.</param>
    /// <param name="ShopId">Javob bergan do'konning takrorlanmas belgisi.</param>
    /// <param name="ShopName">Do'kon nomi — faqat ko'rsatish uchun.</param>
    public sealed record Result(string? Problem, string? ShopId = null, string? ShopName = null);

    /// <summary>Hammasi joyida bo'lsa <c>Problem</c> <c>null</c>.</summary>
    public static async Task<Result> ProbeAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/health", ct);
            if (response.IsSuccessStatusCode)
            {
                var (id, name) = await ReadShopAsync(response, ct);
                return new Result(null, id, name);
            }

            // 400 — deyarli har doim AllowedHosts: server lokal tarmoqqa
            // ochilmagan. Buni alohida aytish kerak, aks holda texnik
            // manzilni qayta-qayta tekshirib vaqt yo'qotadi.
            if ((int)response.StatusCode == 400)
                return new Result("Server ulanishni rad etdi. Server kassada tarmoq rejimi yoqilmagan.");

            return new Result($"Server javob berdi, lekin xato bilan ({(int)response.StatusCode}).");
        }
        catch (TaskCanceledException)
        {
            return new Result("Server javob bermadi. Kompyuter o'chiq yoki tarmoqda yo'q.");
        }
        catch (HttpRequestException ex)
        {
            return new Result("Ulanib bo'lmadi: " + Explain(ex));
        }
    }

    /// <summary>Faqat holat kerak bo'lganda — qisqa yo'l.</summary>
    public static async Task<string?> CheckAsync(string baseUrl, CancellationToken ct) =>
        (await ProbeAsync(baseUrl, ct)).Problem;

    /// <summary>
    /// Javob bergan server AYNAN o'sha do'konmi.
    /// </summary>
    /// <remarks>
    /// <para>Belgi hali saqlanmagan bo'lsa (eski sozlama) tekshiruv
    /// o'tkazilmaydi — aks holda yangilanishdan keyin ishlab turgan
    /// kassalar to'xtab qolardi. Belgi birinchi muvaffaqiyatli ulanishda
    /// yoziladi.</para>
    /// </remarks>
    public static string? ShopMismatch(string? expectedShopId, Result probe)
    {
        if (string.IsNullOrWhiteSpace(expectedShopId)) return null;
        if (probe.Problem is not null) return null;
        // Eski versiyadagi server belgini qaytarmaydi — uni xato deb
        // hisoblash ishlab turgan do'konni to'xtatib qo'yardi.
        if (string.IsNullOrWhiteSpace(probe.ShopId)) return null;
        if (string.Equals(probe.ShopId, expectedShopId, StringComparison.OrdinalIgnoreCase)) return null;

        var name = string.IsNullOrWhiteSpace(probe.ShopName) ? "boshqa do'kon" : $"«{probe.ShopName}»";
        return "Bu manzilda BOSHQA do'kon turibdi — " + name + "."
            + Environment.NewLine + Environment.NewLine
            + "Ulanish to'xtatildi: aks holda savdolar va qoldiqlar begona do'konning "
            + "bazasiga yozilardi."
            + Environment.NewLine + Environment.NewLine
            + "Odatda sababi — server kassaning manzili o'zgargan (routerdan yangi IP olgan) "
            + "va eski manzil boshqa kompyuterga o'tgan. Sozlashda yangi manzilni kiriting.";
    }

    private static async Task<(string? Id, string? Name)> ReadShopAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            return (Text(root, "shopId"), Text(root, "shopName"));
        }
        catch (Exception)
        {
            // Javobni o'qib bo'lmasligi ULANISHNI to'xtatmaydi: server
            // javob bergan, demak u tirik. Eski versiyada bu maydonlar
            // umuman yo'q.
            return (null, null);
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
    }

    private static string Explain(HttpRequestException ex) => ex.InnerException switch
    {
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused }
            => "server kompyuterida Buildix ochiq emas.",
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.TimedOut }
            => "brandmauer to'sib turgan bo'lishi mumkin.",
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound }
            => "bunday manzil tarmoqda topilmadi.",
        _ => "manzilni va tarmoqni tekshiring.",
    };
}
