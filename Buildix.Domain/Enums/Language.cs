namespace Buildix.Domain.Enums;

/// <summary>UI language. Persisted as int — explicit values are the DB contract.</summary>
public enum Language
{
    Uzbek = 0,  // uz
    Russian = 1, // ru
    English = 2 // en
}

/// <summary>
/// Conversion between <see cref="Language"/> and the two-letter code the web
/// client speaks ("uz" / "ru" / "en").
///
/// This lives in one place on purpose: the codes used to be produced ad hoc at
/// each call site and they disagreed — the login response emitted "uz"/"ru"
/// while <c>UserDto</c> emitted <c>Language.ToString().ToLowerInvariant()</c>,
/// i.e. "uzbek"/"russian", which no client could match against its locale list.
/// </summary>
public static class LanguageCodes
{
    /// <summary>Fallback for unknown/absent values — the shop's default language.</summary>
    public const string Default = "uz";

    public static string ToCode(this Language language) => language switch
    {
        Language.Uzbek => "uz",
        Language.Russian => "ru",
        Language.English => "en",
        _ => Default,
    };

    /// <summary>
    /// Parse a client-supplied code. Accepts both the code ("uz") and the enum
    /// name ("Uzbek"), case-insensitively; returns null when unrecognised so
    /// callers can decide between "leave unchanged" and "use the default".
    /// </summary>
    public static Language? FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return code.Trim().ToLowerInvariant() switch
        {
            "uz" or "uzbek" => Language.Uzbek,
            "ru" or "russian" => Language.Russian,
            "en" or "english" => Language.English,
            _ => null,
        };
    }
}
