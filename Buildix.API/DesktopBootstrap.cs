using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Buildix.API;

/// <summary>
/// Do'kon kompyuterida ilovani birinchi marta ishga tayyorlash.
///
/// <para>Bulutda sirlar muhit o'zgaruvchilaridan keladi va ularni odam
/// qo'yadi. Do'konda bunday odam yo'q: omborchi ilovani o'rnatadi va
/// ochadi, xolos. Shuning uchun kerakli sirlar shu yerda, birinchi ishga
/// tushishda yaratiladi.</para>
///
/// <para><b>Nega o'rnatuvchiga tikib qo'yilmaydi.</b> Bitta o'rnatuvchidan
/// chiqarilgan JWT kaliti bilan har qanday do'konning tokenini
/// soxtalashtirish mumkin bo'lardi — bitta nusxani ochgan odam hammasiga
/// kalit topgan bo'lardi. Bu yerda esa har kompyuterning kaliti boshqacha
/// va u hech qachon tarmoqqa chiqmaydi.</para>
/// </summary>
public static class DesktopBootstrap
{
    /// <summary>JWT kaliti uchun bayt uzunligi (base64 da ~88 belgi).</summary>
    private const int JwtKeyBytes = 64;

    /// <summary>
    /// Lokal sozlama faylini tekshiradi va yetishmayotgan sirlarni yaratadi.
    /// Fayl allaqachon bo'lsa — mavjud qiymatlar SAQLANADI: kalit almashsa
    /// barcha kassirlarning sessiyasi uzilib qolardi.
    /// </summary>
    public static void EnsureLocalSecrets(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        if (File.Exists(path))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                // Fayl buzilgan bo'lsa uni JIMGINA almashtirmaymiz: ichida
                // tiklab bo'lmaydigan kalit bor edi va uni yo'qotish barcha
                // sessiyalarni uzadi. To'xtaymiz va sababini aytamiz.
                throw new InvalidOperationException(
                    $"Lokal sozlama fayli buzilgan: {path}. Uni tuzating yoki o'chiring " +
                    "(o'chirilsa yangi kalit yaratiladi va hamma qaytadan kirishi kerak bo'ladi).");
            }
        }
        else
        {
            root = new JsonObject();
        }

        var changed = false;

        var jwt = root["Jwt"] as JsonObject;
        if (jwt is null) { jwt = new JsonObject(); root["Jwt"] = jwt; changed = true; }

        if (string.IsNullOrWhiteSpace(jwt["Key"]?.GetValue<string>()))
        {
            jwt["Key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(JwtKeyBytes));
            changed = true;
        }

        if (!changed) return;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        // Avval vaqtinchalik faylga yozib, keyin o'rniga qo'yamiz: yozish
        // o'rtasida elektr uzilsa yarim fayl qolmasin.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
