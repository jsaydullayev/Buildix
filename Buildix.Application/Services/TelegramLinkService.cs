using System.Security.Cryptography;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// <see cref="ITelegramLinkService"/> — bir martalik kod bilan bog'lash.
///
/// <para>Kod 6 xonali va faqat 10 daqiqa yashaydi. Uni <b>chat egasi</b> botdan
/// oladi, panelda esa <b>hisob egasi</b> kiritadi — ikkalasi bir odam bo'lganda
/// bog'lanish o'tadi. Chat ID ning o'zi hech qachon foydalanuvchidan
/// so'ralmaydi.</para>
/// </summary>
public class TelegramLinkService : ITelegramLinkService
{
    /// <summary>Kod amal qilish muddati. Qisqa — kod ochiq chatda qoladi.</summary>
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    /// <summary>Ishlatilgan qatorlar audit uchun shuncha turadi, keyin tozalanadi.</summary>
    private static readonly TimeSpan UsedRetention = TimeSpan.FromDays(30);
    private const int MaxAttempts = 5;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    /// <summary>Bo'sh kod izlashga urinishlar — 10^6 fazoda amalda 1 tasi yetadi.</summary>
    private const int GenerateTries = 20;

    private readonly IAppDbContext _context;
    private readonly ITashkentClock _clock;
    private readonly ILogger<TelegramLinkService> _logger;

    public TelegramLinkService(IAppDbContext context, ITashkentClock clock, ILogger<TelegramLinkService> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task<(string Code, DateTime ExpiresAtUtc)> IssueCodeAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // Tozalash: ishlatilmagan-muddati o'tgan qator kod raqamini qisman
        // unikal indeksda band qilib turadi, ya'ni uni o'chirmasak 10^6 fazo
        // asta-sekin to'lib borardi.
        var stale = _context.TelegramLinkCodes
            .Where(c => (c.UsedAtUtc == null && c.ExpiresAtUtc < now)
                     || (c.UsedAtUtc != null && c.UsedAtUtc < now - UsedRetention));
        if (IsRelational)
        {
            await stale.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            // InMemory (test provayderi) bulk operatsiyani bilmaydi.
            _context.TelegramLinkCodes.RemoveRange(await stale.ToListAsync(cancellationToken));
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Amaldagi kod bo'lsa — AYNAN o'shani qaytaramiz. Har xabarga yangi kod
        // bersak, foydalanuvchi eskisini kiritib "kod noto'g'ri" olardi.
        var active = await _context.TelegramLinkCodes
            .Where(c => c.ChatId == chatId && c.UsedAtUtc == null && c.ExpiresAtUtc > now)
            .OrderByDescending(c => c.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (active is not null)
            return (active.Code, active.ExpiresAtUtc);

        for (var attempt = 0; attempt < GenerateTries; attempt++)
        {
            // Kriptografik generator: kodni oldindan aytib bo'lmasin.
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            if (await _context.TelegramLinkCodes.AnyAsync(c => c.Code == code && c.UsedAtUtc == null, cancellationToken))
                continue;

            var row = new TelegramLinkCode
            {
                Id = Guid.NewGuid(),
                Code = code,
                ChatId = chatId,
                CreatedAt = now,
                ExpiresAtUtc = now.Add(CodeLifetime),
            };
            _context.TelegramLinkCodes.Add(row);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Ikki chat bir vaqtda bir xil kodni tanladi — qatorni tashlab,
                // boshqasini olamiz.
                _context.Entry(row).State = EntityState.Detached;
                continue;
            }
            return (row.Code, row.ExpiresAtUtc);
        }

        throw new InvalidOperationException("Kod yaratib bo'lmadi. Birozdan keyin qayta urinib ko'ring.");
    }

    public async Task<long> ConsumeAsync(User user, string code, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        // Urinishlar oynasi eskirgan bo'lsa yangidan boshlanadi.
        var attempts = user.TelegramLinkAttempts;
        var windowEnd = user.TelegramLinkAttemptsResetUtc;
        if (windowEnd is null || windowEnd < now)
        {
            attempts = 0;
            windowEnd = now.Add(AttemptWindow);
        }
        if (attempts >= MaxAttempts)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((windowEnd.Value - now).TotalMinutes));
            throw new InvalidOperationException($"Juda ko'p urinish. {minutes} daqiqadan keyin qayta urinib ko'ring.");
        }

        var normalized = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (normalized.Length != 6)
            await FailAsync(user, attempts + 1, windowEnd.Value,
                "Kod 6 xonali bo'lishi kerak. Botga xabar yozing — u kodni yuboradi.");

        var row = await _context.TelegramLinkCodes
            .FirstOrDefaultAsync(c => c.Code == normalized && c.UsedAtUtc == null && c.ExpiresAtUtc > now, cancellationToken);
        if (row is null)
            await FailAsync(user, attempts + 1, windowEnd.Value,
                "Kod noto'g'ri yoki muddati tugagan. Botga yozib, yangi kod oling.");

        // Chat allaqachon band bo'lishi mumkin: kod berilgandan keyin, u
        // ishlatilgunicha o'sha chatni boshqa hisob bog'lab olgan bo'lsa.
        var taken = await _context.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.TelegramChatId == row!.ChatId && u.Id != user.Id, cancellationToken);
        if (taken)
            await FailAsync(user, attempts + 1, windowEnd.Value,
                "Bu Telegram akkaunt boshqa foydalanuvchiga biriktirilgan.");

        // SaveChanges chaqirilmaydi — qator "ishlatilgan" deb belgilanadi va
        // chaqiruvchining o'z saqlashi bilan bitta tranzaksiyada yoziladi.
        row!.UsedAtUtc = now;
        row.UsedByUserId = user.Id;
        user.TelegramLinkAttempts = 0;
        user.TelegramLinkAttemptsResetUtc = null;

        _logger.LogInformation("Telegram link redeemed by user {UserId}", user.Id);
        return row.ChatId;
    }

    /// <summary>
    /// Urinishni sanab, xatoni ko'taradi.
    ///
    /// Hisoblagich <c>ExecuteUpdate</c> bilan DARHOL yoziladi: chaqiruvchi
    /// istisnodan keyin <c>SaveChangesAsync</c> qilmaydi (va qilmasligi ham
    /// kerak — profilning boshqa o'zgarishlari rad etilgan so'rovda saqlanmaydi),
    /// ya'ni kuzatiladigan obyektga yozilgan hisoblagich yo'qolib ketardi va
    /// cheklov umuman ishlamasdi. <see cref="CancellationToken.None"/> —
    /// mijoz so'rovni uzib yuborib cheklovdan qutulmasin.
    /// </summary>
    private async Task FailAsync(User user, int attempts, DateTime windowEnd, string message)
    {
        user.TelegramLinkAttempts = attempts;
        user.TelegramLinkAttemptsResetUtc = windowEnd;
        if (IsRelational)
            await _context.Users.IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.TelegramLinkAttempts, attempts)
                    .SetProperty(u => u.TelegramLinkAttemptsResetUtc, (DateTime?)windowEnd),
                    CancellationToken.None);
        else
            // InMemory (test provayderi): bulk update yo'q — kuzatiladigan
            // holatni saqlaymiz. Faqat testlarda, shuning uchun "boshqa
            // o'zgarishlar ham yoziladi" nomuvofiqligi ahamiyatsiz.
            await _context.SaveChangesAsync(CancellationToken.None);
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Postgres'mi yoki test InMemory provayderimi. Bulk (<c>ExecuteUpdate</c> /
    /// <c>ExecuteDelete</c>) operatsiyalar InMemory'da qo'llab-quvvatlanmaydi —
    /// loyihadagi mavjud provayder tekshiruvlari bilan bir xil uslub.
    /// </summary>
    private bool IsRelational => _context.Database.ProviderName?.Contains("InMemory") == false;

    // PostgreSQL unikal buzilishi — Application qatlamiga Npgsql paketini
    // olib kirmaslik uchun SQLSTATE xabar matnidan o'qiladi
    // (RegistrationRequestService'dagi bilan bir xil usul).
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message?.Contains("23505") == true;
}
