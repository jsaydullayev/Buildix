using System.Text.Json;

namespace Buildix.Application.Common;

/// <summary>
/// Do'kon dasturining chiqarilgan versiyasini YUKLANGAN fayllardan
/// aniqlaydi.
///
/// <para><b>Nega sozlamadan emas.</b> Ilgari versiya <c>.env</c> da qo'lda
/// yozilardi (<c>DESKTOP_VERSION=1.0.1</c>). Bu ikkita mustaqil manba
/// degani edi: papkada 1.0.3 turishi, sozlamada esa 1.0.1 qolishi
/// mumkin — va sahifa egaga YOLG'ON versiya ko'rsatardi. Xato hech
/// qanday belgi bermasdi: tugma ishlar, fayl yuklanardi, faqat yonidagi
/// raqam noto'g'ri bo'lardi. Har chiqarishda ikkinchi joyni yangilashni
/// unutish esa vaqt masalasi.</para>
///
/// <para>Endi manba bitta: <c>releases.win.json</c> — o'rnatuvchi bilan
/// yonma-yon yotgan va yangilanish mexanizmi ham AYNAN shu faylni
/// o'qiydigan ro'yxat. Papkaga yangi paket qo'yilishi bilan sahifadagi
/// raqam o'zi o'zgaradi.</para>
/// </summary>
public static class DesktopRelease
{
    /// <summary>
    /// O'rnatuvchi manzilidan uning papkasini ajratadi.
    ///
    /// <para>Papka nomi ataylab taxmin qilib bo'lmaydigan qilib qo'yilgan
    /// va u faqat manzilda uchraydi, shuning uchun shu yerdan olinadi —
    /// ikkinchi sozlama kaliti yana ikkinchi manba bo'lardi.</para>
    ///
    /// <para>Masalan
    /// <c>https://buildix.uz/updates/a1b2c3/Buildix-win-Setup.exe</c> →
    /// <c>a1b2c3</c>.</para>
    /// </summary>
    public static string? FolderFromUrl(string? installerUrl)
    {
        if (string.IsNullOrWhiteSpace(installerUrl)) return null;

        // Manzil to'liq bo'lmasligi ham mumkin (nisbiy yo'l), shuning uchun
        // Uri ga tayanmaymiz — bizga faqat oxirgi ikki bo'lak kerak.
        var path = installerUrl.Split('?')[0].Split('#')[0].TrimEnd('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var folder = parts[^2];
        // Ichma-ich yo'l bilan papkadan chiqib ketishga urinishni to'samiz:
        // qiymat sozlamadan keladi va u fayl yo'liga qo'shiladi.
        if (folder is "." or ".." || folder.Contains('\\') || folder.Contains(':')) return null;

        return folder;
    }

    /// <summary>
    /// <c>releases.win.json</c> tarkibidan eng yangi versiyani oladi.
    ///
    /// <para>Ro'yxatda bitta versiyaning to'liq va farqli (delta) paketlari
    /// birga yotadi, ustiga eskilari ham qoladi — shuning uchun eng katta
    /// raqam tanlanadi, birinchisi emas. Fayl buzilgan yoki bo'sh bo'lsa
    /// <c>null</c> qaytadi va chaqiruvchi eski xulqqa tushadi.</para>
    /// </summary>
    public static string? VersionFromReleases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Assets", out var assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            Version? best = null;
            string? bestText = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("Version", out var v)
                    || v.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = v.GetString();
                // Matn bo'yicha emas, RAQAM bo'yicha solishtiramiz: "1.0.10"
                // matnda "1.0.9" dan kichik chiqadi va sahifa eski versiyani
                // ko'rsatib turardi.
                if (!Version.TryParse(text, out var parsed)) continue;
                if (best is not null && parsed <= best) continue;

                best = parsed;
                bestText = text;
            }

            return bestText;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
