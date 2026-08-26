using System.Security.Cryptography;
using System.Text;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Do'kon kompyuterini bulutga bog'laydi.
///
/// <para><b>Oqim.</b> Panelda do'kon uchun kod olinadi → texnik uni ilovaga
/// kiritadi → ilova kodni kalitga almashtiradi. Shundan keyin ilova bulut
/// bilan faqat shu kalit orqali gaplashadi va kod boshqa kerak emas.</para>
///
/// <para><b>Nega bir martalik.</b> Kod qog'ozga yoziladi, telefonda aytiladi
/// va suhbat tarixida qolib ketadi. Doimiy bo'lsa, uni ko'rgan har kim
/// istalgan vaqtda do'kon ma'lumotini so'rab oladigan kompyuter qo'sha
/// olardi. Bir marta ishlatilgach kod o'ladi.</para>
/// </summary>
public class TerminalPairingService : ITerminalPairingService
{
    /// <summary>
    /// Adashtiradigan belgilarsiz alifbo: 0/O va 1/I/L yo'q. Kod telefonda
    /// aytiladi va qog'ozdan ko'chiriladi — aynan shu juftliklar xato beradi.
    /// </summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Bir sutka — texnik do'konga bugun yetib bormasa, ertaga yangi kod oladi.</summary>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(24);

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TerminalPairingService> _logger;
    private readonly TimeProvider _clock;

    public TerminalPairingService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        ILogger<TerminalPairingService> logger,
        TimeProvider? clock = null)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Do'kon uchun yangi kod beradi. Eski ishlatilmagan kodlar bekor
    /// qilinadi: ikkita amaldagi kod bo'lsa, qaysi biri berilganini hech kim
    /// eslay olmaydi va eskisi qog'ozda qolib ketardi.
    /// </summary>
    public async Task<Result<PairingCodeDto>> IssueCodeAsync(
        int marketId, Guid byUserId, CancellationToken ct = default)
    {
        var market = await _context.Markets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == marketId, ct);
        if (market is null)
            return Result.Failure<PairingCodeDto>("Do'kon topilmadi", "NOT_FOUND");

        var now = _clock.GetUtcNow().UtcDateTime;

        var live = await _context.TerminalPairingCodes
            .IgnoreQueryFilters()
            .Where(c => c.MarketId == marketId && c.UsedAtUtc == null && c.ExpiresAtUtc > now)
            .ToListAsync(ct);
        foreach (var stale in live) stale.ExpiresAtUtc = now;

        var code = new TerminalPairingCode
        {
            Id = Guid.NewGuid(),
            Code = NewCode(),
            MarketId = marketId,
            ExpiresAtUtc = now + CodeLifetime,
            CreatedByUserId = byUserId,
        };
        _context.TerminalPairingCodes.Add(code);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Pairing code issued for market {MarketId} by {UserId}, expires {Expires:O}",
            marketId, byUserId, code.ExpiresAtUtc);

        return Result.Success(new PairingCodeDto(code.Code, code.ExpiresAtUtc, market.Name));
    }

    /// <summary>
    /// Kodni kalitga almashtiradi. Kalit FAQAT shu yerda, faqat bir marta
    /// qaytariladi — bazada uning hash'i qoladi.
    /// </summary>
    public async Task<Result<PairedTerminalDto>> RedeemAsync(
        string code, string terminalName, string? ipAddress, CancellationToken ct = default)
    {
        var normalised = Normalise(code);
        if (normalised.Length == 0)
            return Result.Failure<PairedTerminalDto>("Kod kiritilmadi");

        var now = _clock.GetUtcNow().UtcDateTime;

        // Bitta tranzaksiyada: kodni topish, uni o'lik deb belgilash va
        // kompyuterni yaratish. Aks holda bir kod ikki marta ishlatilishi
        // mumkin edi — ikki kompyuter bir vaqtda urinsa.
        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var row = await _context.TerminalPairingCodes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Code == normalised, ct);

            // Xato sababi ATAYLAB aytilmaydi: «kod topilmadi», «muddati
            // o'tgan» va «allaqachon ishlatilgan» uchun bitta javob. Farqni
            // ko'rsatish taxmin qiluvchiga qaysi kodlar mavjudligini
            // aytib berardi.
            if (row is null || row.UsedAtUtc is not null || row.ExpiresAtUtc <= now)
            {
                _logger.LogWarning("Pairing rejected for code ending {Tail} from {Ip}",
                    normalised.Length >= 4 ? normalised[^4..] : "?", ipAddress ?? "?");
                return Result.Failure<PairedTerminalDto>(
                    "Kod noto'g'ri yoki muddati o'tgan. Panelda yangi kod oling.");
            }

            var market = await _context.Markets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == row.MarketId, ct);
            if (market is null)
                return Result.Failure<PairedTerminalDto>("Do'kon topilmadi", "NOT_FOUND");

            var key = NewKey();
            var terminal = new ShopTerminal
            {
                Id = Guid.NewGuid(),
                MarketId = row.MarketId,
                Name = string.IsNullOrWhiteSpace(terminalName) ? "Kassa" : terminalName.Trim(),
                KeyHash = HashKey(key),
                LastSeenAtUtc = now,
                LastIpAddress = ipAddress,
            };
            _context.ShopTerminals.Add(terminal);

            row.UsedAtUtc = now;
            row.UsedByTerminalId = terminal.Id;

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Terminal {TerminalId} paired to market {MarketId} from {Ip}",
                terminal.Id, terminal.MarketId, ipAddress ?? "?");

            return Result.Success(new PairedTerminalDto(
                terminal.Id, terminal.MarketId, market.Name, key));
        });
    }

    /// <summary>
    /// Kalit bo'yicha kompyuterni topadi. Kalit noto'g'ri, bekor qilingan
    /// yoki umuman yo'q bo'lsa — null.
    /// </summary>
    public async Task<ShopTerminal?> AuthenticateAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var hash = HashKey(key);
        var terminal = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.KeyHash == hash, ct);

        return terminal is { RevokedAtUtc: null } ? terminal : null;
    }

    /// <summary>Sakkiz belgi, ikki bo'lakka ajratilgan: BX-4K7P-92MC.</summary>
    private static string NewCode()
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return $"BX-{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    /// <summary>
    /// Kiritilgan kodni tozalaydi: chiziqcha, bo'shliq va harf registri
    /// ahamiyatsiz. Texnik uni qanday yozsa ham ishlashi kerak.
    /// </summary>
    private static string Normalise(string input)
    {
        var kept = new StringBuilder();
        foreach (var ch in input.ToUpperInvariant())
        {
            if (Alphabet.Contains(ch)) kept.Append(ch);
        }

        // «BX» prefiksining harflari ham alifboda (B va X) — ular yuqoridagi
        // filtrdan o'tib ketadi. Shuning uchun ular ATAYLAB shu yerda
        // olib tashlanadi: aks holda tozalangan satr 8 emas, 10 belgi
        // bo'lib qolar va hech qanday kod tanilmasdi.
        var body = kept.ToString();
        if (body.Length == 10 && body.StartsWith("BX", StringComparison.Ordinal))
            body = body[2..];

        return body.Length == 8 ? $"BX-{body[..4]}-{body[4..]}" : string.Empty;
    }

    /// <summary>32 bayt — taxmin qilib bo'lmaydigan kalit.</summary>
    private static string NewKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static string HashKey(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
