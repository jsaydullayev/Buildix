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

            var created = await CreateTerminalAsync(row.MarketId, terminalName, ipAddress, now, ct);
            if (created.IsFailure) return created;

            row.UsedAtUtc = now;
            row.UsedByTerminalId = created.Value.TerminalId;

            await _unitOfWork.SaveChangesAsync(ct);
            return created;
        });
    }

    /// <summary>
    /// Do'kon egasining login-paroli bilan bog'laydi — kodsiz.
    ///
    /// <para><b>Nega kerak.</b> Kodni faqat SuperAdmin bera olardi, ya'ni
    /// yangi kompyuterni ishga tushirish uchun do'kon egasi platformaga
    /// murojaat qilishga majbur edi. Egasi o'z hisobiga allaqachon ega —
    /// undan boshqa isbot so'rashning ma'nosi yo'q.</para>
    ///
    /// <para><b>Kim chaqiradi.</b> Faqat <c>Owner</c> — buni chaqiruvchi
    /// (kontroller) tekshiradi, chunki parolni tekshirish o'sha yerda.
    /// Kassir ham bog'lay olsa, o'g'irlangan bitta parol butun do'kon
    /// bazasini begona kompyuterga ko'chirish imkonini berardi.</para>
    /// </summary>
    public async Task<Result<PairedTerminalDto>> ActivateAsync(
        int marketId, string terminalName, string? ipAddress, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var created = await CreateTerminalAsync(marketId, terminalName, ipAddress, now, ct);
            if (created.IsFailure) return created;

            await _unitOfWork.SaveChangesAsync(ct);
            return created;
        });
    }

    /// <summary>
    /// Do'konni tekshirib, unga yangi kompyuter yozadi. Saqlash CHAQIRUVCHIDA
    /// — kod oqimi shu bilan birga kodni ham o'lik deb belgilashi kerak, va
    /// bu ikkisi bitta tranzaksiyada bo'lishi shart.
    /// </summary>
    private async Task<Result<PairedTerminalDto>> CreateTerminalAsync(
        int marketId, string terminalName, string? ipAddress, DateTime now, CancellationToken ct)
    {
        var market = await _context.Markets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == marketId, ct);
        if (market is null)
            return Result.Failure<PairedTerminalDto>("Do'kon topilmadi", "NOT_FOUND");

        // O'chirilgan do'kon uchun kompyuter bog'lanmaydi. Kod berilgandan
        // keyin do'kon o'chirilgan bo'lishi mumkin va o'shanda bog'lanish
        // hech qachon ishlatilmaydigan kalit yaratardi.
        if (!market.IsActive)
            return Result.Failure<PairedTerminalDto>("Do'kon o'chirilgan.", "NOT_FOUND");

        // ── Bitta do'kon — bitta baza ───────────────────────────────────
        // Kalit ishlaydigan kompyuter — bu do'konning MA'LUMOTLAR BAZASI
        // turgan joy. Ikkitasi bo'lsa, bitta do'kon nomidan ikkita
        // mustaqil baza ish ko'radi: ikkalasi ham o'z chek raqamlarini
        // beradi, o'z qoldig'ini yuritadi va bulutga bir-birining ustiga
        // yozadi. Bu pul va qoldiq ma'lumotini JIMGINA buzadi — hech
        // qanday xato chiqmaydi, faqat raqamlar to'g'ri kelmay qoladi.
        //
        // Ikkinchi va uchinchi kassa bulutga umuman bog'lanmaydi: ular
        // server kassadagi API ga lokal tarmoq orqali ulanadi va o'z
        // kalitiga muhtoj emas.
        //
        // Shuning uchun eskisini AVTOMATIK bekor qilmaymiz: eski
        // kompyuterda hali yuborilmagan savdolar qolgan bo'lishi mumkin
        // va ularni jimgina yo'qotib bo'lmaydi. Operator ataylab bekor
        // qilsin.
        var active = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.MarketId == marketId && t.RevokedAtUtc == null, ct);
        if (active is not null)
        {
            return Result.Failure<PairedTerminalDto>(
                $"Bu do'konga «{active.Name}» kompyuteri allaqachon bog'langan. "
                + "Yangisini bog'lashdan oldin panelda eskisini bekor qiling — "
                + "unda yuborilmagan savdolar qolgan bo'lishi mumkin.",
                "ALREADY_PAIRED");
        }

        var key = NewKey();
        var terminal = new ShopTerminal
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Name = string.IsNullOrWhiteSpace(terminalName) ? "Kassa" : terminalName.Trim(),
            KeyHash = HashKey(key),
            LastSeenAtUtc = now,
            LastIpAddress = ipAddress,
        };
        _context.ShopTerminals.Add(terminal);

        _logger.LogInformation(
            "Terminal {TerminalId} paired to market {MarketId} from {Ip}",
            terminal.Id, terminal.MarketId, ipAddress ?? "?");

        return Result.Success(new PairedTerminalDto(
            terminal.Id, terminal.MarketId, market.Name, key));
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

    /// <summary>
    /// Do'konga bog'langan kompyuterlar ro'yxati — panel uchun.
    ///
    /// <para>Bekor qilinganlari ham qaytadi: operator «qachon va nimani
    /// bekor qilganman» degan savolga javob topa olishi kerak.</para>
    /// </summary>
    public async Task<IReadOnlyList<TerminalDto>> ListAsync(int marketId, CancellationToken ct = default)
    {
        var rows = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .Where(t => t.MarketId == marketId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(t => new TerminalDto(
            t.Id, t.Name, t.CreatedAt, t.LastSeenAtUtc, t.RevokedAtUtc, t.LastIpAddress)).ToList();
    }

    /// <summary>
    /// Kalitni bekor qiladi — kompyuter shu zahoti bulutdan uziladi.
    ///
    /// <para><b>Qachon kerak.</b> Kompyuter almashtirilganda yoki
    /// yo'qolganda. Bekor qilish qaytarilmaydi: kalitning o'zi hech qayerda
    /// saqlanmaydi, ya'ni uni «qayta yoqib» bo'lmaydi — yangi bog'lanish
    /// kerak bo'ladi.</para>
    ///
    /// <para><b>Eski kompyuterdagi ma'lumot.</b> Bekor qilingandan keyin u
    /// yuborilmagan savdolarni bulutga jo'nata olmaydi. Shuning uchun buni
    /// faqat ataylab, eski kompyuter bilan ishi tugagach qilish kerak.</para>
    /// </summary>
    public async Task<Result<bool>> RevokeAsync(
        Guid terminalId, Guid byUserId, CancellationToken ct = default)
    {
        var terminal = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == terminalId, ct);

        if (terminal is null)
            return Result.Failure<bool>("Kompyuter topilmadi", "NOT_FOUND");

        // Takroriy bekor qilish xato emas: operator ro'yxatni yangilamasdan
        // ikki marta bosishi mumkin va bu hech narsani buzmaydi.
        if (terminal.RevokedAtUtc is not null) return Result.Success(true);

        terminal.RevokedAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Terminal {TerminalId} ({Name}) of market {MarketId} revoked by {UserId}",
            terminal.Id, terminal.Name, terminal.MarketId, byUserId);

        return Result.Success(true);
    }

    /// <summary>
    /// Aloqa vaqtini belgilaydi.
    ///
    /// <para><b>Har so'rovda EMAS.</b> Sinxronizatsiya tez-tez takrorlanadi va
    /// har chaqiruvda yozish bazani keraksiz yuklardi. Bir daqiqadan tez
    /// yangilashning ma'nosi ham yo'q: bu maydon «do'kon uch kundan beri
    /// aloqaga chiqmayapti» degan savolga javob beradi, soniyalarni
    /// o'lchamaydi.</para>
    /// </summary>
    public async Task TouchAsync(ShopTerminal terminal, string? ipAddress, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (terminal.LastSeenAtUtc is { } last && now - last < TimeSpan.FromMinutes(1)) return;

        terminal.LastSeenAtUtc = now;
        if (!string.IsNullOrWhiteSpace(ipAddress)) terminal.LastIpAddress = ipAddress;
        await _unitOfWork.SaveChangesAsync(ct);
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
