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
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

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
