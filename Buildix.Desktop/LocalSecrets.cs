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

    public LocalSecrets()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Buildix", "desktop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        if (File.Exists(_path))
        {
            try
            {
                _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
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
        // Avval vaqtinchalik faylga, keyin o'rniga: yozish o'rtasida elektr
        // uzilsa yarim fayl qolmasin.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}
