using System.Net.Http;

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
/// yiqilgan» holatni ham ushlaydi.</para>
/// </summary>
public static class ServerProbe
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>Hammasi joyida bo'lsa <c>null</c>, aks holda tushunarli xato matni.</summary>
    public static async Task<string?> CheckAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/health", ct);
            if (response.IsSuccessStatusCode) return null;

            // 400 — deyarli har doim AllowedHosts: server lokal tarmoqqa
            // ochilmagan. Buni alohida aytish kerak, aks holda texnik
            // manzilni qayta-qayta tekshirib vaqt yo'qotadi.
            if ((int)response.StatusCode == 400)
                return "Server ulanishni rad etdi. Server kassada tarmoq rejimi yoqilmagan.";

            return $"Server javob berdi, lekin xato bilan ({(int)response.StatusCode}).";
        }
        catch (TaskCanceledException)
        {
            return "Server javob bermadi. Kompyuter o'chiq yoki tarmoqda yo'q.";
        }
        catch (HttpRequestException ex)
        {
            return "Ulanib bo'lmadi: " + Explain(ex);
        }
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
