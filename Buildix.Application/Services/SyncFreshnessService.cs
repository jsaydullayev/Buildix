using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Bu raqamlar qachongi?» degan savolga javob beradi.
///
/// <para><b>Nima uchun kerak.</b> Egasi telefonda ko'radigan ma'lumot
/// do'kondan sinxronizatsiya orqali keladi. Do'kon internetsiz ishlayotgan
/// bo'lsa, ekrandagi son eskirgan bo'ladi — lekin u ESKIRGANDEK
/// KO'RINMAYDI. Egasi shu raqamga qarab qaror qabul qiladi: qarzni undirish,
/// tovar buyurtma qilish, kassirni chaqirish. Eskirgan sonni jonli deb
/// ko'rsatish uni yashirishdan ham yomon.</para>
///
/// <para><b>Yuborilmagan cheklar soni ATAYLAB yo'q.</b> Uni bulut bila
/// olmaydi: do'kon aloqada bo'lmasa hech narsa ayta olmaydi, aloqada bo'lsa
/// esa navbat allaqachon bo'shagan bo'ladi. Ya'ni «7 ta chek yuborilmagan»
/// degan raqam faqat DO'KON ekranida ma'noga ega. Bu yerda esa yagona rost
/// narsa — oxirgi aloqa vaqti.</para>
/// </summary>
public class SyncFreshnessService : ISyncFreshnessService
{
    /// <summary>
    /// Shu vaqtdan keyin ma'lumot «eskirgan» deb belgilanadi.
    ///
    /// <para>Do'kon har besh daqiqada aloqaga chiqadi, ya'ni bir-ikki
    /// o'tkazib yuborish normal holat — internet bir zumga uzilishi mumkin.
    /// O'n besh daqiqa esa allaqachon o'tkazib yuborishdan ko'ra ko'proq
    /// narsani anglatadi.</para>
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly IAppDbContext _context;
    private readonly TimeProvider _clock;

    public SyncFreshnessService(IAppDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<SyncFreshnessDto> GetAsync(int marketId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();

        var terminal = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .Where(t => t.MarketId == marketId && t.RevokedAtUtc == null)
            .Select(t => new { t.Name, t.LastSeenAtUtc })
            .FirstOrDefaultAsync(ct);

        if (terminal is null)
        {
            // Do'kon kompyuteri hali bog'lanmagan. Bu «aloqa yo'q» dan
            // BOSHQA holat va uni shunday deb ko'rsatish kerak: bu yerda
            // kutish emas, o'rnatishni tugatish talab qilinadi.
            return new SyncFreshnessDto(false, false, null, null, null);
        }

        if (terminal.LastSeenAtUtc is not { } lastSeen)
            return new SyncFreshnessDto(true, false, null, null, terminal.Name);

        var lastSeenUtc = new DateTimeOffset(
            DateTime.SpecifyKind(lastSeen, DateTimeKind.Utc), TimeSpan.Zero);
        var age = now - lastSeenUtc;

        return new SyncFreshnessDto(
            IsPaired: true,
            IsFresh: age <= StaleAfter,
            LastSyncAtUtc: lastSeenUtc,
            SecondsSinceSync: (long)Math.Max(0, age.TotalSeconds),
            TerminalName: terminal.Name);
    }
}
