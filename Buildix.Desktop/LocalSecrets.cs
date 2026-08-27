using System.Text.Json;
using System.Text.Json.Nodes;

namespace Buildix.Desktop;

/// <summary>
/// Shu kompyuterga tegishli sirlar (baza paroli). Birinchi so'ralganda
/// yaratiladi va shundan keyin o'zgarmaydi.
///
/// <para><b>Nega API ning faylidan alohida.</b> API o'z sirini
/// <c>local.json</c> ga yozadi. Ikkalasi bitta faylga yozsa, ular bir vaqtda
/// ochilganda bir-birining yozuvini o'chirib yuborishi mumkin edi — natijada
/// baza paroli yo'qolar va ilova ochilmay qolardi. Alohida fayl bu xavfni
/// butunlay yo'q qiladi.</para>
/// </summary>
public sealed class LocalSecrets
{
    private readonly string _path;
    private readonly JsonObject _root;

    /// <summary>
    /// Fayl shu ishga tushishda yangidan yaratildimi.
    ///
    /// <para>Muhim: baza paroli faqat shu faylda saqlanadi, bazaning o'zida
    /// esa uning hash'i yotadi va uni qaytarib bo'lmaydi. Ya'ni fayl
    /// yo'qolgan, lekin baza mavjud bo'lsa — yangi parol bilan unga
    /// ULANIB BO'LMAYDI. Buni oldindan aniqlab, tushunarli xabar berish
    /// kerak: aks holda foydalanuvchi PostgreSQL ning rus tilidagi
    /// «проверка подлинности не пройдена» xabarini ko'radi va nima
    /// qilishni bilmaydi.</para>
    /// </summary>
    public bool CreatedNow { get; }

    public LocalSecrets()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Buildix", "desktop.json");
        var folder = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(folder);

        // Butun papka cheklanadi, faqat sirlar fayli emas. Ichida baza
        // fayllari va zaxira nusxalar yotadi — ular ham do'kon ma'lumoti.
        // Bu yerda, eng boshida: keyin yaratiladigan hamma narsa meros
        // orqali himoyalangan bo'lib tug'iladi.
        try { SecretFile.RestrictDirectory(folder); }
        catch (UnauthorizedAccessException) { /* huquq yo'q — ilova baribir ishlaydi */ }

        CreatedNow = !File.Exists(_path);

        if (File.Exists(_path))
        {
            try
            {
                _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
                // Oldingi versiya huquqlarni cheklamagan bo'lishi mumkin.
                SecretFile.Restrict(_path);
                return;
            }
            catch (JsonException)
            {
                // Buzilgan faylni JIMGINA almashtirmaymiz: ichida baza paroli
                // bor va uni yo'qotish bazani ochib bo'lmas holga keltiradi.
                throw new InvalidOperationException(
                    $"Sozlama fayli buzilgan: {_path}\n\n" +
                    "Uni tuzating. O'chirilsa yangi parol yaratiladi va mavjud bazani ochib bo'lmaydi.");
            }
        }
        _root = new JsonObject();
    }

    /// <summary>
    /// Yangilanish serverining manzili. Sozlamada bo'lmasa — <c>null</c> va
    /// yangilanish umuman tekshirilmaydi.
    ///
    /// <para>Ataylab sirlar fayliga qo'yilgan: har do'kon o'z kanalidan
    /// (masalan sinov yoki asosiy) yangilanishi mumkin va buni o'rnatuvchi
    /// qayta yig'masdan o'zgartirsa bo'ladi.</para>
    /// </summary>
    public string? UpdateFeedUrl =>
        _root["UpdateFeedUrl"] is JsonValue v && v.TryGetValue<string>(out var url)
        && !string.IsNullOrWhiteSpace(url)
            ? url
            : null;

    /// <summary>
    /// Bu kompyuter ULANADIGAN server kassa manzili. <c>null</c> bo'lsa —
    /// bu kompyuterning O'ZI server: bazani va API ni u ko'taradi.
    ///
    /// <para><b>Nega sukut bo'yicha null.</b> Do'konlarning ko'pchiligida bitta
    /// kassa bor va ular hech narsa sozlamasligi kerak. Ikkinchi va uchinchi
    /// kassa esa ataylab sozlanadi — <c>Buildix.Desktop.exe --setup</c>.</para>
    ///
    /// <para><b>Nega bitta bazaga uchta kassa.</b> Har kassada o'z bazasi
    /// bo'lsa, ikkisi bir vaqtda oxirgi qop sementni sotib yuborardi va chek
    /// raqamlari to'qnashardi: qoldiq qulfi ham, raqam qulfi ham faqat bitta
    /// baza ichida ishlaydi.</para>
    /// </summary>
    public string? ServerUrl =>
        _root["ServerUrl"] is JsonValue v && v.TryGetValue<string>(out var url)
        && !string.IsNullOrWhiteSpace(url)
            ? url
            : null;

    /// <summary>
    /// Bulut manzili — do'kon o'z xodimlarini va obuna holatini shu yerdan
    /// oladi. Bog'lanish paytida yoziladi.
    /// </summary>
    public string? CloudUrl =>
        _root["CloudUrl"] is JsonValue v && v.TryGetValue<string>(out var url)
        && !string.IsNullOrWhiteSpace(url)
            ? url
            : null;

    /// <summary>
    /// Shu kompyuterning bulutdagi kaliti.
    ///
    /// <para><b>Nega aynan shu faylda.</b> Fayl huquqlari cheklangan
    /// (SYSTEM, Administratorlar va foydalanuvchi), ya'ni kassir o'z hisobidan
    /// uni o'qiy olmaydi. Kalit bilan butun do'kon ma'lumotini — savdolar,
    /// mijozlar, parol hash'lari — bulutdan so'rab olish mumkin, shuning
    /// uchun u parol bilan bir xil darajada himoyalanishi kerak.</para>
    ///
    /// <para>Nashr sozlamasiga yozilmaydi: u har yangilanishda almashadi va
    /// kalit jimgina yo'qolardi.</para>
    /// </summary>
    public string? TerminalKey =>
        _root["TerminalKey"] is JsonValue v && v.TryGetValue<string>(out var key)
        && !string.IsNullOrWhiteSpace(key)
            ? key
            : null;

    /// <summary>Bog'lanish natijasini saqlaydi.</summary>
    public void SetCloudPairing(string url, string terminalKey)
    {
        _root["CloudUrl"] = url.Trim().TrimEnd('/');
        _root["TerminalKey"] = terminalKey;
        Save();
    }

    /// <summary>Server manzilini yozadi; <c>null</c> — bu kompyuter server bo'ladi.</summary>
    public void SetServerUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            _root.Remove("ServerUrl");
        else
            _root["ServerUrl"] = url.Trim().TrimEnd('/');
        Save();
    }

    /// <summary>
    /// API ni lokal tarmoqqa ochish. Sukut bo'yicha YOPIQ — bitta kassali
    /// do'konda uni ochish keraksiz xavf.
    ///
    /// <para><b>Nega bu yerda, <c>appsettings.Desktop.json</c> da emas.</b>
    /// Nashr sozlamasi har yangilanishda almashadi, ya'ni u yerga yozilgan
    /// qiymat jimgina yo'qolar va bir kun kelib boshqa kassalar ulana olmay
    /// qolardi — sababi esa hech qayerda ko'rinmasdi. Bu fayl esa
    /// kompyuterniki: yangilanish unga tegmaydi.</para>
    /// </summary>
    public bool AllowLan =>
        _root["AllowLan"] is JsonValue v && v.TryGetValue<bool>(out var on) && on;

    public void SetAllowLan(bool allow)
    {
        if (allow) _root["AllowLan"] = true;
        else _root.Remove("AllowLan");
        Save();
    }

    /// <summary>
    /// Yorliq printerining Windows'dagi nomi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kerak.</b> Do'konda ikkita printer bo'ladi: chek uchun
    /// va yorliq uchun. Brauzerning chop etish oynasi har safar sukut
    /// bo'yicha printerni tanlaydi va omborchi uni qo'lda almashtirishga
    /// majbur bo'lardi — kuniga o'nlab marta. Nom shu yerda bir marta
    /// saqlanadi va yorliq to'g'ridan-to'g'ri o'sha printerga ketadi.</para>
    ///
    /// <para>Bo'sh bo'lsa oyna ochiladi (avvalgi xulq) — ya'ni sozlanmagan
    /// do'konda ham ish to'xtamaydi.</para>
    /// </remarks>
    public string? LabelPrinter =>
        _root["LabelPrinter"] is JsonValue v && v.TryGetValue<string>(out var name)
        && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

    public void SetLabelPrinter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) _root.Remove("LabelPrinter");
        else _root["LabelPrinter"] = name.Trim();
        Save();
    }

    /// <summary>Kalit bo'yicha sirni oladi; bo'lmasa <paramref name="create"/> bilan yaratadi.</summary>
    public string GetOrCreate(string key, Func<string> create)
    {
        if (_root[key] is JsonValue v && v.TryGetValue<string>(out var existing)
            && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var value = create();
        _root[key] = value;
        Save();
        return value;
    }

    private void Save()
    {
        var json = _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        // SecretFile atomik yozadi VA huquqlarni cheklaydi: bu faylda baza
        // paroli bor, ProgramData esa sukut bo'yicha hammaga o'qishga ochiq.
        SecretFile.Write(_path, json);
    }
}
