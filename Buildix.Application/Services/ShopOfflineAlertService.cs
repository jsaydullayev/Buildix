using Buildix.Application.Interfaces;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Do'kon uzoq vaqt aloqaga chiqmasa egasiga xabar beradi.
///
/// <para><b>Nima uchun kerak.</b> Ekrandagi belgi ma'lumot eskirganini
/// aytadi — lekin buning uchun egasi ilovani ochishi kerak. Do'kon
/// kompyuteri ertalab yonmagan yoki interneti uzilgan bo'lsa, egasi buni
/// kechqurun biladi va kun bo'yi ko'r bo'lib qoladi.</para>
///
/// <para><b>Asosiy xavf — keraksiz vahima.</b> Do'kon kechasi yopiladi va
/// kompyuter o'chiriladi: bu NORMAL holat. Har kecha xabar yuborilsa, egasi
/// bir haftada bildirishnomalarni o'chirib qo'yadi — va o'shanda haqiqiy
/// nosozlikni ham ko'rmaydi. Shuning uchun uchta cheklov bor: faqat kunduzi,
/// faqat uch soatdan uzoq sukutda va bir do'kon uchun sutkada bir marta.</para>
/// </summary>
public class ShopOfflineAlertService : IShopOfflineAlertService
{
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromHours(3);

    /// <summary>
    /// Takroriy xabar oralig'i. Bir sutkadan sal kam: do'kon har kuni bir xil
    /// vaqtda tekshirilmaydi va qat'iy 24 soat bo'lsa, ertangi xabar bir soat
    /// «erta» bo'lib o'tkazib yuborilardi.
    /// </summary>
    private static readonly TimeSpan RepeatAfter = TimeSpan.FromHours(20);

    private const int QuietBeforeHour = 10;
    private const int QuietAfterHour = 20;

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITelegramNotifier _telegram;
    private readonly ITashkentClock _clock;
    private readonly ILogger<ShopOfflineAlertService> _logger;

    public ShopOfflineAlertService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        ITelegramNotifier telegram,
        ITashkentClock clock,
        ILogger<ShopOfflineAlertService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _telegram = telegram;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Bir o'tish. Nechta do'kon haqida xabar yuborilgani qaytadi.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // Kechasi aloqa yo'qligi kutilgan holat — tekshirishning ma'nosi yo'q.
        var localHour = _clock.NowLocal.Hour;
        if (localHour < QuietBeforeHour || localHour >= QuietAfterHour) return 0;

        var now = _clock.UtcNow;
        var offlineSince = now - OfflineThreshold;
        var repeatBefore = now - RepeatAfter;

        // Bloklangan va o'chirilgan do'konlar tashlab ketiladi: ularning
        // aloqada emasligi kutilgan va bu haqda xabar berish egasini
        // chalg'itardi.
        var silent = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .Where(t => t.RevokedAtUtc == null
                     && (t.LastSeenAtUtc == null || t.LastSeenAtUtc < offlineSince)
                     && (t.LastOfflineAlertAtUtc == null || t.LastOfflineAlertAtUtc < repeatBefore))
            .Join(_context.Markets.IgnoreQueryFilters(),
                  t => t.MarketId, m => m.Id, (t, m) => new { Terminal = t, Market = m })
            .Where(x => x.Market.IsActive && !x.Market.IsBlocked)
            .ToListAsync(ct);

        foreach (var shop in silent)
        {
            var since = shop.Terminal.LastSeenAtUtc is { } seen
                ? $"{(now - seen).TotalHours:F0} soatdan beri"
                : "hali bir marta ham";

            await _telegram.SendToOwnerAsync(
                shop.Market.Id,
                $"⚠️ <b>{shop.Market.Name}</b>\n\n"
                + $"Do'kon kompyuteri ({shop.Terminal.Name}) {since} bulut bilan aloqaga chiqmadi.\n\n"
                + "Telefondagi raqamlar o'sha paytdagi holatni ko'rsatadi. "
                + "Kompyuter yonganini va internet ishlayotganini tekshiring.",
                ct);

            // Vaqt xabar YETIB BORMAGAN bo'lsa ham belgilanadi: Telegram
            // bog'lanmagan do'kon uchun har soatda qayta urinish jurnalni
            // to'ldirar va foyda bermasdi.
            shop.Terminal.LastOfflineAlertAtUtc = now;

            _logger.LogWarning(
                "Shop {MarketId} ({Name}) offline since {LastSeen:O} — owner notified",
                shop.Market.Id, shop.Market.Name, shop.Terminal.LastSeenAtUtc);
        }

        if (silent.Count > 0) await _unitOfWork.SaveChangesAsync(ct);
        return silent.Count;
    }
}
