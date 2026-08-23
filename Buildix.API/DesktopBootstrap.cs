using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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
                // Oldingi versiya huquqlarni cheklamagan bo'lishi mumkin.
                Restrict(path);
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
        // Avval vaqtinchalik faylga yozib, huquqlarni O'SHA yerda cheklab,
        // keyin o'rniga qo'yamiz. Ikki sabab: yozish o'rtasida elektr uzilsa
        // yarim fayl qolmasin, va fayl bir lahza ham hammaga ochiq turmasin.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        Restrict(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Faylni faqat egasiga ochiq qoldiradi.
    ///
    /// <para><c>%ProgramData%</c> sukut bo'yicha «Users» guruhiga o'qishga
    /// ochiq. Ichida JWT imzo kaliti bor: uni o'qigan odam istalgan
    /// foydalanuvchining tokenini yasashi mumkin. Kassirning o'z Windows
    /// hisobi bo'lsa, u ilovaga umuman kirmasdan egasi bo'lib qolardi.</para>
    /// </summary>
    private static void Restrict(string path)
    {
        // Bulutda (Linux konteyner) bu kod ishlamaydi va kerak ham emas —
        // u yerda sir muhit o'zgaruvchisidan keladi, faylda saqlanmaydi.
        if (!OperatingSystem.IsWindows()) return;

        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, AccessControlType.Allow));

        var me = WindowsIdentity.GetCurrent().User;
        if (me is not null)
        {
            acl.AddAccessRule(new FileSystemAccessRule(
                me, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        new FileInfo(path).SetAccessControl(acl);
    }
}
