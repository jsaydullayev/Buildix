using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
/// <para><b>Ikki xil ekran, ikki xil savol.</b> Do'kon kompyuterida ma'lumot
/// bazaning o'zida turadi va u har doim jonli — u yerda «eskirgan» degan
/// tushuncha ma'nosiz. Do'kondagi savol boshqacha: «egasi telefonda
/// ko'rayotgan raqamlar yangimi». Bulutda esa savol to'g'ridan-to'g'ri:
/// «men ko'rayotgan raqam qachongi».</para>
///
/// <para><b>Yuborilmagan cheklar soni ATAYLAB yo'q.</b> Uni bulut bila
/// olmaydi: do'kon aloqada bo'lmasa hech narsa ayta olmaydi, aloqada bo'lsa
/// esa navbat allaqachon bo'shagan bo'ladi.</para>
/// </summary>
public class SyncFreshnessService : ISyncFreshnessService
{
    /// <summary>
    /// Shu vaqtdan keyin ma'lumot «eskirgan» deb belgilanadi.
    ///
    /// <para>Do'kon har daqiqada aloqaga chiqadi, ya'ni bir-ikki o'tkazib
    /// yuborish normal holat — internet bir zumga uzilishi mumkin. O'n besh
    /// daqiqa esa allaqachon o'tkazib yuborishdan ko'ra ko'proq narsani
    /// anglatadi.</para>
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    private readonly IAppDbContext _context;
    private readonly TimeProvider _clock;
    private readonly bool _isShopMachine;

    public SyncFreshnessService(IAppDbContext context, IConfiguration configuration, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
        _isShopMachine = configuration.GetValue<bool>("Desktop:Enabled");
    }

    public Task<SyncFreshnessDto> GetAsync(int marketId, CancellationToken ct = default) =>
        _isShopMachine ? ShopViewAsync(ct) : CloudViewAsync(marketId, ct);

    /// <summary>
    /// DO'KON kompyuteridagi ko'rinish — do'konning O'Z sinxronizatsiya
    /// holatidan.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari bu yerda ham bulutdagi kabi <c>ShopTerminals</c> jadvali
    /// o'qilardi. Lekin o'sha jadval do'kon bazasiga HECH QACHON yozilmaydi
    /// (tortish faqat market va xodimlarni olib keladi), ya'ni har doim bo'sh
    /// bo'lardi va natijada kassirning har ekranida doimiy qizil «do'kon
    /// kompyuteri bulutga bog'lanmagan» chizig'i turardi — do'kon aslida
    /// bog'langan va ishlab turgan bo'lsa ham.</para>
    /// </remarks>
    private async Task<SyncFreshnessDto> ShopViewAsync(CancellationToken ct)
    {
        var state = await _context.SyncStates
            .AsNoTracking()
            .Select(s => new { s.LastPushedAtUtc, s.LastPushError, s.LastError })
            .FirstOrDefaultAsync(ct);

        // Hali bulutdan hech narsa tortilmagan — do'kon bog'lanmagan.
        if (state is null) return new SyncFreshnessDto(false, false, null, null, null);

        var error = state.LastPushError ?? state.LastError;

        if (state.LastPushedAtUtc is not { } pushed)
            return new SyncFreshnessDto(true, false, null, null, null, error, IsShopMachine: true);

        var pushedUtc = new DateTimeOffset(
            DateTime.SpecifyKind(pushed, DateTimeKind.Utc), TimeSpan.Zero);
        var age = _clock.GetUtcNow() - pushedUtc;

        return new SyncFreshnessDto(
            IsPaired: true,
            // Xato bo'lsa «yangi» deb aytish mumkin emas, hatto oxirgi
            // muvaffaqiyat yaqinda bo'lsa ham: aynan shu holatda
            // sinxronizatsiya to'xtab qolgan bo'ladi.
            IsFresh: error is null && age <= StaleAfter,
            LastSyncAtUtc: pushedUtc,
            SecondsSinceSync: (long)Math.Max(0, age.TotalSeconds),
            TerminalName: null,
            Error: error,
            IsShopMachine: true);
    }

    /// <summary>
    /// BULUTDAGI ko'rinish — egasi telefonda yoki brauzerda ko'radi.
    /// </summary>
    /// <remarks>
    /// <para>Yangilik <see cref="Domain.Entities.ShopTerminal.LastPushAtUtc"/>
    /// bo'yicha hisoblanadi, <c>LastSeenAtUtc</c> bo'yicha EMAS. Ikkinchisi
    /// kalit tekshiruvidan o'tishda qo'yiladi va push'ning o'zi yiqilganda
    /// ham yangilanardi — natijada bulutga haftalab ma'lumot kelmasa ham
    /// ekranda yashil «hozirgina sinxron» yozuvi turardi.</para>
    /// </remarks>
    private async Task<SyncFreshnessDto> CloudViewAsync(int marketId, CancellationToken ct)
    {
        var terminal = await _context.ShopTerminals
            .IgnoreQueryFilters()
            .Where(t => t.MarketId == marketId && t.RevokedAtUtc == null)
            .Select(t => new { t.Name, t.LastSeenAtUtc, t.LastPushAtUtc })
            .FirstOrDefaultAsync(ct);

        if (terminal is null)
        {
            // Do'kon kompyuteri hali bog'lanmagan. Bu «aloqa yo'q» dan
            // BOSHQA holat va uni shunday deb ko'rsatish kerak: bu yerda
            // kutish emas, o'rnatishni tugatish talab qilinadi.
            return new SyncFreshnessDto(false, false, null, null, null);
        }

        if (terminal.LastPushAtUtc is not { } pushed)
        {
            // Bog'langan, lekin hali birorta yozuv kelmagan. Aloqa bor-yo'qligi
            // bu yerda ahamiyatsiz — ko'rsatiladigan ma'lumot yo'q.
            return new SyncFreshnessDto(true, false, null, null, terminal.Name);
        }

        var pushedUtc = new DateTimeOffset(
            DateTime.SpecifyKind(pushed, DateTimeKind.Utc), TimeSpan.Zero);
        var age = _clock.GetUtcNow() - pushedUtc;

        // Aloqa bor, lekin ma'lumot kelmayapti — eng yashirin nosozlik.
        // Do'kon har daqiqada bulutga murojaat qiladi (LastSeenAtUtc yangi),
        // lekin yuborish tashqi kalit xatosi bilan yiqiladi. Bu holatni
        // alohida aytmasak, u yashil belgi ostida ko'rinmay qolardi.
        string? error = null;
        if (age > StaleAfter && terminal.LastSeenAtUtc is { } seen)
        {
            var seenUtc = new DateTimeOffset(
                DateTime.SpecifyKind(seen, DateTimeKind.Utc), TimeSpan.Zero);
            if (_clock.GetUtcNow() - seenUtc <= StaleAfter)
                error = "Do'kon aloqada, lekin ma'lumot kelmayapti — sinxronizatsiya to'xtagan.";
        }

        return new SyncFreshnessDto(
            IsPaired: true,
            IsFresh: age <= StaleAfter,
            LastSyncAtUtc: pushedUtc,
            SecondsSinceSync: (long)Math.Max(0, age.TotalSeconds),
            TerminalName: terminal.Name,
            Error: error);
    }
}
