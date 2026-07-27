using System.Text.Json;
using System.Text.Json.Serialization;

namespace Buildix.API.Serialization;

/// <summary>
/// JSON converter that renders a stored UTC <see cref="DateTime"/> in GMT+5
/// (Tashkent time) on the wire. This is a presentation/serialization concern,
/// so it lives in the API layer — not Domain (K4: Domain stays framework-free).
/// The database stores UTC; the client receives GMT+5.
/// </summary>
public class TashkentTimeJsonConverter : JsonConverter<DateTime>
{
    // Uzbekistan is a permanent UTC+5 (no DST). Use a fixed offset so the
    // result is identical on Windows and Linux/Docker and never depends on
    // the host OS timezone database. NOTE: the Windows ID "Central Asia
    // Standard Time" is UTC+6 (Astana) — it pushed every timestamp 1h ahead.
    private static readonly TimeZoneInfo TashkentTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "Uzbekistan Time (UTC+5)", "UZT");

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetDateTime(out var dateTime))
            return Normalize(dateTime);

        return default;
    }

    /// <summary>
    /// Offset bilan kelgan qiymatni UTC ga keltiradi.
    ///
    /// <para><c>Utf8JsonReader.TryGetDateTime</c> «2026-08-25T10:00:00+00:00»
    /// ni <c>Kind=Local</c> qilib qaytaradi — ya'ni natija SERVER vaqt
    /// mintaqasiga bog'liq bo'lib qoladi, va Npgsql bunday qiymatni
    /// <c>timestamp with time zone</c> ustuniga yozishdan bosh tortadi (500).
    /// Bir xil lahzani UTC ko'rinishida qaytaramiz: qiymat o'zgarmaydi, faqat
    /// Kind to'g'rilanadi.</para>
    ///
    /// <para><c>Unspecified</c> ATAYLAB tegilmaydi: mijoz «2026-08-01» kabi
    /// faqat KUN yuborganda uni vaqt mintaqasiga ko'ra surish sanani bir kunga
    /// siljitib yuborardi. Bunday qiymatlarni servislar o'zi UTC deb
    /// belgilaydi (DebtService, SaleService).</para>
    /// </summary>
    internal static DateTime Normalize(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Convert UTC to Tashkent time (GMT+5) before writing to JSON
        DateTime tashkentTime;

        if (value.Kind == DateTimeKind.Utc)
        {
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(value, TashkentTimeZone);
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            var utc = value.ToUniversalTime();
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(utc, TashkentTimeZone);
        }
        else
        {
            // Unspecified - assume UTC
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(value, TashkentTimeZone);
        }

        writer.WriteStringValue(tashkentTime);
    }
}

/// <summary>
/// Nullable <see cref="DateTime"/> version of <see cref="TashkentTimeJsonConverter"/>.
/// </summary>
public class TashkentTimeJsonConverterNullable : JsonConverter<DateTime?>
{
    private static readonly TimeZoneInfo TashkentTimeZone =
        TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "Uzbekistan Time (UTC+5)", "UZT");

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TryGetDateTime(out var dateTime))
            return TashkentTimeJsonConverter.Normalize(dateTime);

        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        DateTime tashkentTime;
        var dateTimeValue = value.Value;

        if (dateTimeValue.Kind == DateTimeKind.Utc)
        {
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(dateTimeValue, TashkentTimeZone);
        }
        else if (dateTimeValue.Kind == DateTimeKind.Local)
        {
            var utc = dateTimeValue.ToUniversalTime();
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(utc, TashkentTimeZone);
        }
        else
        {
            tashkentTime = TimeZoneInfo.ConvertTimeFromUtc(dateTimeValue, TashkentTimeZone);
        }

        writer.WriteStringValue(tashkentTime);
    }
}
