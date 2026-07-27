using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Do'kon egalariga obuna bo'yicha xabar — <b>Telegram orqali</b>.
///
/// <para><b>Nega SMS emas.</b> SMS har xabar uchun pul turadi, yetib borgani
/// tasdiqlanmaydi va matn provayder jurnalida qoladi. Telegram kanali bu
/// loyihada allaqachon qurilgan: bog'lanish bir martalik kod bilan
/// TASDIQLANGAN (<c>TelegramLinkCode</c>), yuborish natijasi
/// (<c>SendToChatAsync</c> → bool) ma'lum, va ega o'sha yerdan hisobot ham
/// so'ray oladi.</para>
///
/// <para><b>Bog'lamagan ega nima bo'ladi.</b> Xabar jimgina yo'qolmaydi:
/// yuborilmaganlar soni qaytariladi va konsol ro'yxatida «Telegram
/// bog'lanmagan» belgisi turadi — operator ularni qo'ng'iroq bilan
/// xabardor qiladi.</para>
/// </summary>
public class SubscriptionNotifierService : ISubscriptionNotifier
{
    private readonly IAppDbContext _context;
    private readonly ITelegramNotifier _telegram;
    private readonly IPlatformSettingsProvider _settings;
    private readonly ITashkentClock _clock;
    private readonly ILogger<SubscriptionNotifierService> _logger;

    public SubscriptionNotifierService(
        IAppDbContext context,
        ITelegramNotifier telegram,
        IPlatformSettingsProvider settings,
        ITashkentClock clock,
        ILogger<SubscriptionNotifierService> logger)
    {
        _context = context;
        _telegram = telegram;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    public async Task<NotifyResult> RemindExpiringAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (!s.NotifyExpiring) return NotifyResult.Empty;

        var nowUtc = _clock.UtcNow;
        var horizon = nowUtc.AddDays(s.ExpiryReminderDays);

        // Muddati yaqinlashganlar. Stamp ExpiresAt ning o'zi bo'lgani uchun
        // bir davrga bitta eslatma ketadi; to'lovdan keyin muddat surilgach
        // stamp mos kelmay qoladi va keyingi davr uchun yana ishlaydi.
        var due = await _context.Markets.IgnoreQueryFilters()
            .Where(m => m.IsActive && !m.IsBlocked
                        && m.ExpiresAt != null
                        && m.ExpiresAt > nowUtc && m.ExpiresAt <= horizon
                        && (m.RenewalReminderSentFor == null || m.RenewalReminderSentFor != m.ExpiresAt))
            .ToListAsync(ct);

        var sent = 0;
        var unreachable = 0;
        foreach (var market in due)
        {
            if (ct.IsCancellationRequested) break;

            var days = Math.Max(0, (int)Math.Ceiling((market.ExpiresAt!.Value - nowUtc).TotalDays));
            var text =
                $"<b>Buildix</b>\nObuna muddati tugayapti.\n\n" +
                $"Do'kon: <b>{Escape(market.Name)}</b>\n" +
                $"Amal qiladi: <b>{_clock.ToLocal(market.ExpiresAt.Value):dd.MM.yyyy}</b> ({days} kun qoldi)\n\n" +
                "Uzilishsiz ishlash uchun to'lovni oldindan amalga oshiring.";

            if (await SendToOwnerAsync(market.Id, text, ct))
            {
                // Faqat YETIB BORGANDA belgilanadi — aks holda bitta jim
                // xatolik butun davr uchun eslatmani yo'q qilardi.
                market.RenewalReminderSentFor = market.ExpiresAt;
                sent++;
            }
            else
            {
                unreachable++;
            }
        }

        if (sent > 0) await _context.SaveChangesAsync(ct);
        if (sent + unreachable > 0)
            _logger.LogInformation("Renewal reminders: sent={Sent} unreachable={Unreachable}", sent, unreachable);
        return new NotifyResult(sent, unreachable);
    }

    public async Task<NotifyResult> RemindOverdueAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        var nowUtc = _clock.UtcNow;

        var overdue = await _context.Markets.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IsActive && !m.IsBlocked && m.ExpiresAt != null && m.ExpiresAt <= nowUtc)
            .Select(m => new { m.Id, m.Name, m.ExpiresAt })
            .ToListAsync(ct);

        var sent = 0;
        var unreachable = 0;
        foreach (var m in overdue)
        {
            if (ct.IsCancellationRequested) break;

            var daysOverdue = (int)Math.Floor((nowUtc - m.ExpiresAt!.Value).TotalDays);
            var stage = daysOverdue <= s.GraceDays
                ? "Hozircha do'kon odatdagidek ishlayapti."
                : s.RestrictAfterGrace
                    ? "Sotuv vaqtincha to'xtatilgan — ma'lumotlar ochiq."
                    : "Iltimos, to'lovni amalga oshiring.";

            var text =
                $"<b>Buildix</b>\nObuna to'lovi kechikdi.\n\n" +
                $"Do'kon: <b>{Escape(m.Name)}</b>\n" +
                $"Muddat tugagan: <b>{_clock.ToLocal(m.ExpiresAt.Value):dd.MM.yyyy}</b> ({daysOverdue} kun oldin)\n\n" +
                stage;

            if (await SendToOwnerAsync(m.Id, text, ct)) sent++;
            else unreachable++;
        }

        _logger.LogInformation("Overdue reminders: sent={Sent} unreachable={Unreachable}", sent, unreachable);
        return new NotifyResult(sent, unreachable);
    }

    public async Task NotifyBlockedAsync(int marketId, string? reason, CancellationToken ct = default)
    {
        if (!_settings.Current.NotifyBlocked) return;

        var text =
            "<b>Buildix</b>\nDo'kon vaqtincha bloklandi.\n\n" +
            (string.IsNullOrWhiteSpace(reason) ? "" : $"Sabab: {Escape(reason)}\n\n") +
            "Kirishni tiklash uchun administrator bilan bog'laning.";

        // Xabar bloklashning O'ZIGA to'sqinlik qilmaydi — u allaqachon
        // yozilgan; bu faqat xabardor qilish.
        await SendToOwnerAsync(marketId, text, ct);
    }

    /// <summary>
    /// Egaga yuboradi va YETIB BORGANINI qaytaradi. <c>SendToOwnerAsync</c>
    /// natijani bermaydi (void), shuning uchun chat id shu yerda topiladi —
    /// «bog'lanmagan ega» ni sanay olishimiz kerak.
    /// </summary>
    private async Task<bool> SendToOwnerAsync(int marketId, string text, CancellationToken ct)
    {
        var chatId = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.MarketId == marketId && u.Role == Role.Owner
                        && u.IsActive && !u.IsDeleted && u.TelegramChatId != null)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.TelegramChatId)
            .FirstOrDefaultAsync(ct);
        if (chatId is null or 0) return false;

        return await _telegram.SendToChatAsync(chatId.Value, text, ct);
    }

    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
