using Buildix.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Buildix.Application.Services;

/// <inheritdoc cref="ICurrentRegisterService"/>
public sealed class CurrentRegisterService : ICurrentRegisterService
{
    /// <summary>Qobiq qo'yadigan sarlavha.</summary>
    public const string HeaderName = "X-Buildix-Register";

    /// <summary>
    /// Belgining eng katta uzunligi.
    /// </summary>
    /// <remarks>
    /// Belgi chek ustida va ro'yxatlarda ko'rinadi, ya'ni u QISQA bo'lishi
    /// kerak — «A», «B», «1». Sarlavhani har kim yozishi mumkin, shuning
    /// uchun uzunlik shu yerda kesiladi: aks holda uzun matn bazaga tushib,
    /// ekranni buzardi.
    /// </remarks>
    public const int MaxLength = 4;

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentRegisterService(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    public string? GetRegisterCode()
    {
        var raw = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var code = raw.Trim();

        // Tekshiruv KESISHDAN OLDIN. Teskarisi bo'lsa «KASSA-2» avval
        // «KASS» ga qisqarar, keyin tekshiruvdan o'tib ketardi — ya'ni
        // noto'g'ri yozilgan belgi jimgina boshqa narsaga aylanib, har bir
        // chekka shu holda tushardi. Yaroqsiz belgi — belgisiz qolgani
        // yaxshiroq.
        if (!code.All(char.IsLetterOrDigit)) return null;

        if (code.Length > MaxLength) code = code[..MaxLength];
        return code.ToUpperInvariant();
    }
}
