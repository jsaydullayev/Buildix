namespace Buildix.Application.Settings;

public class JwtSetting
{
    /// <summary>
    /// Minimum HS256 signing-key length (bytes). Enforced at startup
    /// (Program.cs) and again when JwtService resolves — single-sourced here so
    /// the two checks can never drift.
    /// </summary>
    public const int MinKeyLength = 32;

    /// <summary>Token issuer (<c>iss</c> claim), validated on every request.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token audience (<c>aud</c> claim), validated on every request.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// HS256 symmetric signing secret. MUST be at least <see cref="MinKeyLength"/>
    /// bytes; supplied via the <c>Jwt__Key</c> env var (prod) or
    /// appsettings.Development.json (dev) — never committed.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Access-token lifetime in minutes.</summary>
    public int AccessTokenExpireMinutes { get; set; } = 30;

    /// <summary>Bitta refresh tokenning harakatsizlik (idle) muddati (kunlarda).</summary>
    public int RefreshTokenExpireDays { get; set; } = 7;

    /// <summary>
    /// Sessiyaning MUTLAQ umri (kunlarda), rotatsiya zanjirining boshlanishidan
    /// (RefreshToken.SessionStartedAt) hisoblanadi.
    ///
    /// RefreshTokenExpireDays har rotatsiyada YANGILANADI — ya'ni u faqat
    /// harakatsiz sessiyani o'ldiradi. Busiz o'g'irlangan tokenni har 7 kunda
    /// bir marta aylantirib, hisobni ABADIY ushlab turish mumkin edi. Bu limit
    /// esa qat'iy: shu muddatdan keyin qayta login majburiy.
    /// </summary>
    public int MaxSessionDays { get; set; } = 30;

    /// <summary>
    /// Rotatsiya poygasi uchun "grace" oynasi (soniyalarda).
    ///
    /// Ikki tab/qurilma bir vaqtda refresh qilsa, yutqazgan so'rov ham tokenni
    /// "IsUsed=true" holatda ko'radi. Bu O'G'IRLIK EMAS. Shu oyna ichida
    /// ishlatilgan token qayta taqdim etilsa — butun oilani bekor qilmaymiz,
    /// balki 409 qaytaramiz va klient yangilangan tokenni o'qib qayta uradi.
    /// Oynadan tashqaridagi qayta ishlatish esa haqiqiy o'g'irlik sifatida
    /// qaraladi va butun zanjir kuydiriladi.
    /// </summary>
    public int RefreshRaceGraceSeconds { get; set; } = 60;
}
