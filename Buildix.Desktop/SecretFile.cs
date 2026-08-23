using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Buildix.Desktop;

/// <summary>
/// Sir saqlanadigan faylni yozadi va uni faqat egasiga ochiq qoldiradi.
///
/// <para><b>Nega kerak.</b> <c>%ProgramData%</c> sukut bo'yicha «Users»
/// guruhiga o'qishga ochiq. Ya'ni JWT imzo kaliti va baza paroli shu papkaga
/// oddiy yozilsa, kompyuterdagi HAR QANDAY hisob ularni o'qiy oladi. Kassir
/// o'z hisobidan kalitni olib, egasining tokenini yasashi mumkin edi —
/// ilovaga umuman kirmasdan.</para>
///
/// <para>Shuning uchun meros qilib olingan huquqlar uziladi va faqat uchtasi
/// qoldiriladi: SYSTEM, Administratorlar va faylni yaratgan foydalanuvchi.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecretFile
{
    /// <summary>
    /// Matnni atomik yozadi va huquqlarni cheklaydi.
    ///
    /// <para>Avval vaqtinchalik faylga yoziladi, huquqlar O'SHA yerda
    /// qo'yiladi va shundan keyingina o'z o'rniga qo'yiladi — aks holda fayl
    /// bir necha millisekund hammaga ochiq turgan bo'lardi.</para>
    /// </summary>
    public static void Write(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        Restrict(tmp);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Mavjud faylning huquqlarini cheklaydi (eski o'rnatishlar uchun).</summary>
    public static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return;

        var info = new FileInfo(path);
        var acl = new FileSecurity();

        // Meros uziladi: ProgramData dan kelgan «Users: o'qish» huquqi
        // qolmasligi kerak.
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sid in new[]
                 {
                     new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                 })
        {
            acl.AddAccessRule(new FileSystemAccessRule(
                sid, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        // Ilovani ishga tushirgan foydalanuvchi — u bo'lmasa ilova o'z
        // faylini o'qiy olmay qoladi.
        var me = WindowsIdentity.GetCurrent().User;
        if (me is not null)
        {
            acl.AddAccessRule(new FileSystemAccessRule(
                me, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        info.SetAccessControl(acl);
    }
}
