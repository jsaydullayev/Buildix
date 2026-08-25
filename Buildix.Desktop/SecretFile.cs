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

    /// <summary>
    /// Butun papkani va uning ichidagi hamma narsani cheklaydi.
    ///
    /// <para><b>Nega fayldan kam emas.</b> Sirlar fayli cheklangan bo'lsa ham
    /// MA'LUMOTNING O'ZI ochiq qolardi: <c>pgdata</c> va zaxira nusxalar
    /// «Users» guruhiga o'qishga ochiq edi, <c>pgdata</c> ga esa yozish ham
    /// mumkin edi. Ya'ni kassir o'z hisobidan butun do'kon bazasini —
    /// savdolar, mijozlar, qarzlar, parol hashlari — fleshkaga ko'chirib
    /// ketishi mumkin edi. Ilovaga umuman kirmasdan.</para>
    ///
    /// <para><b>Meros bilan.</b> Huquqlar ichkariga tarqaladi, ya'ni keyin
    /// yaratiladigan zaxira nusxa va baza fayllari ham himoyalangan bo'lib
    /// tug'iladi — buni har safar eslab qolish kerak emas.</para>
    ///
    /// <para><b>Cheklov.</b> Faqat ilovani ishga tushirgan Windows hisobi
    /// kira oladi. Bu yangi cheklov emas: baza paroli allaqachon shu tarzda
    /// cheklangan, ya'ni ikkinchi Windows hisobi ilovani baribir ochа
    /// olmasdi.</para>
    /// </summary>
    public static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(path)) return;

        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var inherit = InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit;

        foreach (var sid in Owners())
        {
            acl.AddAccessRule(new FileSystemAccessRule(
                sid, FileSystemRights.FullControl, inherit,
                PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(path).SetAccessControl(acl);
    }

    /// <summary>
    /// Jurnal faylini yangilashdan oldin oldingi nusxasini saqlab qo'yadi va
    /// o'sha yo'lni qaytaradi.
    ///
    /// <para><b>Nega kerak.</b> Jurnal har ochilishda noldan yozilardi.
    /// Ilova ochilmay qolganda esa foydalanuvchi birinchi navbatda uni QAYTA
    /// OCHADI — va aynan shu bilan sababni ko'rsatadigan yagona faylni
    /// o'chirib yuborardi. Endi oldingi ishga tushish jurnali
    /// <c>.prev</c> nomi bilan qoladi.</para>
    ///
    /// <para>Faqat bitta avlod saqlanadi: ikkitasi kamdan-kam kerak bo'ladi,
    /// cheksiz o'sish esa do'kon diskini to'ldirardi.</para>
    /// </summary>
    public static string RotateLog(string path)
    {
        try
        {
            if (File.Exists(path)) File.Move(path, path + ".prev", overwrite: true);
        }
        catch (IOException) { /* band bo'lsa — ustiga yozamiz */ }
        catch (UnauthorizedAccessException) { }
        return path;
    }

    /// <summary>SYSTEM, Administratorlar va joriy foydalanuvchi.</summary>
    private static IEnumerable<IdentityReference> Owners()
    {
        yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        // Ilovani ishga tushirgan foydalanuvchi — u bo'lmasa ilova o'z
        // fayllarini o'qiy olmay qoladi.
        var me = WindowsIdentity.GetCurrent().User;
        if (me is not null) yield return me;
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
