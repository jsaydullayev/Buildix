using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Buildix.Application.Common;

/// <summary>
/// Do'kondan bulutga yuboriladigan yozuvlarni JSON ga aylantiradi.
///
/// <para><b>Nega entity'ning o'zi, DTO emas.</b> Bulutdan do'konga tushadigan
/// ma'lumot uchun aniq DTO yozilgan va u to'g'ri qaror edi: u yerda do'kon
/// bulutdan NIMA olishini cheklash kerak. Bu yo'nalish teskari — bu
/// jadvallarda haqiqat manbai DO'KON, ya'ni uning har bir ustuni bulutga
/// borishi KERAK. Qo'lda yozilgan DTO esa yangi ustun qo'shilganda jimgina
/// eskirar va o'sha ustun bulutga hech qachon yetib bormasdi. Oltita
/// jadval uchun bu 97 ta maydon — har biri e'tibordan chetda qolish
/// joyi.</para>
///
/// <para><b>Navigatsiya xossalari tashlanadi.</b> <c>Sale.SaleItems</c> →
/// <c>SaleItem.Sale</c> — bular halqa hosil qiladi va serializator cheksiz
/// aylanardi. Bog'lanishlar baribir ID ustunlari orqali uzatiladi, ya'ni
/// hech narsa yo'qolmaydi.</para>
///
/// <para><b>Bu ISHONCH degani emas.</b> Bulut kelgan qiymatlarni ko'r-ko'rona
/// qabul qilmaydi: <c>MarketId</c> har doim kalitdan aniqlangan do'konga
/// MAJBURAN almashtiriladi — aks holda bir do'kon boshqasining ma'lumotini
/// buzib yuborishi mumkin edi.</para>
/// </summary>
public static class EntityWireFormat
{
    private const string EntityNamespace = "Buildix.Domain.Entities";

    /// <summary>
    /// Sanani doim UTC sifatida o'qiydi va yozadi.
    ///
    /// <para><c>Unspecified</c> UTC deb qabul qilinadi: bu kanaldagi hamma
    /// vaqt bazadan keladi va u yerda UTC saqlanadi. Uni shundayligicha
    /// qoldirish Npgsql ni <c>timestamp with time zone</c> ustuniga yozishdan
    /// bosh tortishga majbur qilardi.</para>
    /// </summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var value = reader.GetDateTime();
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
            writer.WriteStringValue(utc.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private sealed class UtcNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private static readonly UtcDateTimeConverter Inner = new();

        public override DateTime? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.Null ? null : Inner.Read(ref reader, typeof(DateTime), options);

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else Inner.Write(writer, value.Value, options);
        }
    }

    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { DropNavigationProperties },
            },
        };
        // Vaqtlar HAR DOIM UTC. Bu bir marta yo'l qo'yilgan xatoning
        // takrorlanmasligi uchun: API qolgan hamma joyda vaqtni Toshkent
        // mintaqasida va belgisiz yuboradi va o'sha o'zgartirgich bu kanalga
        // ham tegib ketsa, sanalar 5 soatga suriladi. Sotuv vaqti 5 soat
        // siljigan bo'lsa, egasining telefonidagi «bugungi tushum» noto'g'ri
        // kun bo'yicha hisoblanardi — va hech qanday xato chiqmasdi.
        options.Converters.Add(new UtcDateTimeConverter());
        options.Converters.Add(new UtcNullableDateTimeConverter());
        return options;
    }

    private static void DropNavigationProperties(JsonTypeInfo type)
    {
        if (type.Type.Namespace != EntityNamespace) return;

        for (var i = type.Properties.Count - 1; i >= 0; i--)
        {
            if (IsNavigation(type.Properties[i])) type.Properties.RemoveAt(i);
        }
    }

    private static bool IsNavigation(JsonPropertyInfo property)
    {
        var propertyType = property.PropertyType;

        // Boshqa entity'ga havola.
        if (propertyType.Namespace == EntityNamespace) return true;

        // Entity'lar to'plami. `string` ham IEnumerable, shuning uchun u
        // ataylab chetlab o'tiladi.
        if (propertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propertyType))
        {
            var item = propertyType.IsGenericType
                ? propertyType.GetGenericArguments().FirstOrDefault()
                : propertyType.GetElementType();
            if (item?.Namespace == EntityNamespace) return true;
        }

        return false;
    }

    /// <summary>
    /// Bitta yozuvning ustunlarini boshqasiga ko'chiradi (navigatsiyasiz).
    ///
    /// <para>Bulut tomonda ishlatiladi: mavjud qatorni kelgan qiymatlar bilan
    /// yangilash uchun. Aks holda har jadval uchun qo'lda «a.X = b.X» qatorlari
    /// yozilar va yangi ustun qo'shilganda ular jimgina eskirardi.</para>
    /// </summary>
    public static void CopyColumns<T>(T source, T target, params string[] skip)
    {
        var skipped = new HashSet<string>(skip, StringComparer.Ordinal);

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;
            if (skipped.Contains(property.Name)) continue;
            if (property.PropertyType.Namespace == EntityNamespace) continue;
            if (property.PropertyType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
            {
                var item = property.PropertyType.IsGenericType
                    ? property.PropertyType.GetGenericArguments().FirstOrDefault()
                    : property.PropertyType.GetElementType();
                if (item?.Namespace == EntityNamespace) continue;
            }

            property.SetValue(target, property.GetValue(source));
        }
    }
}
