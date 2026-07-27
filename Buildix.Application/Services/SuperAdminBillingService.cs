using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Подписки и оплаты» — tariflar, muddatlar va qo'lda qabul qilinadigan
/// to'lovlar.
///
/// <para>Otsrochka (grace) va «muddati yaqin» chegarasi <b>platforma
/// sozlamalaridan</b> olinadi — konsolda o'zgartirilgan qiymat obuna eshigiga
/// ham, to'lov matematikasiga ham BIR VAQTDA ta'sir qilishi shart, aks holda
/// eshik 10 kun ochiq turib, to'lov 6-kunni «uzilgan» deb hisoblardi.</para>
///
/// <para><b>Muddatni uzaytirish qoidasi (V4 — grace'ga qarab langar).</b>
/// Yangi muddat <c>langar + N oy</c>. Langar:</para>
/// <list type="bullet">
///   <item>xizmat UZILMAGAN bo'lsa (muddat kelajakda yoki otsrochka ichida) —
///   eski <c>ExpiresAt</c>. Ya'ni erta to'lagan kun yo'qotmaydi, otsrochkada
///   ishlagan do'kon esa o'sha kunlar uchun to'laydi va hisob kuni surilmaydi;</item>
///   <item>xizmat UZILGAN bo'lsa (otsrochka ham o'tgan) — bugun. O'chiq turgan
///   davr uchun pul olinmaydi.</item>
/// </list>
/// </summary>
public class SuperAdminBillingService : ISuperAdminBillingService
{
    private readonly IAppDbContext _context;
    private readonly ITashkentClock _clock;
    private readonly IAuditLogService _audit;
    private readonly IPlatformSettingsProvider _settings;

    public SuperAdminBillingService(
        IAppDbContext context,
        ITashkentClock clock,
        IAuditLogService audit,
        IPlatformSettingsProvider settings)
    {
        _context = context;
        _clock = clock;
        _audit = audit;
        _settings = settings;
    }

    /// <summary>
    /// V4 langari. Ochiq (pure) funksiya — preview va yozuv AYNAN shu
    /// hisobdan foydalanadi, aks holda operator ko'rgan sana bilan yozilgani
    /// bir-biridan farq qilishi mumkin edi.
    /// </summary>
    public static (DateTime NewExpiry, bool AnchoredOnExpiry) Extend(
        DateTime? currentExpiry, int months, DateTime nowUtc, int graceDays)
    {
        var servedThrough = currentExpiry?.AddDays(graceDays);
        var anchored = currentExpiry.HasValue && nowUtc <= servedThrough!.Value;
        var anchor = anchored ? currentExpiry!.Value : nowUtc;
        return (anchor.AddMonths(months), anchored);
    }

    public async Task<IReadOnlyList<SaPlanDto>> PlansAsync(CancellationToken ct = default)
    {
        var plans = await _context.PlatformPlans.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);
        var counts = (await _context.Markets.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.IsActive)
                .GroupBy(m => m.Plan)
                .Select(g => new { Plan = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Plan, x => x.Count);

        return plans
            .Select(p => new SaPlanDto(
                p.Code.ToString(), p.PriceUzs, p.MaxUsers, p.MaxPoints,
                counts.TryGetValue(p.Code, out var c) ? c : 0))
            .ToList();
    }

    public async Task<IReadOnlyList<SaBillingRowDto>> ListAsync(CancellationToken ct = default)
    {
        var nowUtc = _clock.UtcNow;
        var soon = nowUtc.AddDays(_settings.Current.SoonThresholdDays);

        var prices = await PriceMapAsync(ct);

        var markets = await _context.Markets.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new { m.Id, m.Name, m.Plan, m.ExpiresAt, m.IsBlocked })
            .ToListAsync(ct);
        if (markets.Count == 0) return Array.Empty<SaBillingRowDto>();

        var ids = markets.Select(m => m.Id).ToList();

        // Egasi Telegramni bog'laganmi — eslatma unga yetib bora oladimi.
        // Bitta so'rovda: ro'yxat qatoriga «bog'lanmagan» belgisi qo'yiladi,
        // operator kimni qo'ng'iroq bilan xabardor qilishini bilib tursin.
        var reachable = (await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.MarketId != null && ids.Contains(u.MarketId.Value)
                            && u.Role == Role.Owner && u.IsActive && !u.IsDeleted
                            && u.TelegramChatId != null)
                .Select(u => u.MarketId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        // Oxirgi to'lov — do'kon boshiga bitta qator («послед. оплата: 28 июня · Click»).
        var lastPayments = (await _context.SubscriptionPayments.AsNoTracking()
                .Where(p => ids.Contains(p.MarketId))
                .GroupBy(p => p.MarketId)
                .Select(g => g.OrderByDescending(p => p.PaidAtUtc).First())
                .ToListAsync(ct))
            .ToDictionary(p => p.MarketId);

        return markets
            .Select(m =>
            {
                lastPayments.TryGetValue(m.Id, out var last);
                return new SaBillingRowDto(
                    m.Id, m.Name, m.Plan.ToString(),
                    prices.TryGetValue(m.Plan, out var price) ? price : 0m,
                    m.ExpiresAt,
                    StatusOf(m.IsBlocked, m.ExpiresAt, nowUtc, soon),
                    last?.PaidAtUtc,
                    last?.Channel.ToString(),
                    reachable.Contains(m.Id));
            })
            .OrderBy(r => r.ExpiresAt ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Muddatni <c>timestamptz</c> uchun UTC ga keltiradi. Brauzer sanani
    /// mintaqa bilan ham, mintaqasiz ham yuborishi mumkin; Npgsql esa
    /// <c>Local</c>/<c>Unspecified</c> ni rad etadi.
    /// </summary>
    private static DateTime NormalizeExpiry(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Qo'lda kiritilgan muddatni tekshiradi: o'tgan sana do'konni to'lov
    /// qabul qilingan zahoti yopib qo'yardi, 10 yildan narisi esa deyarli
    /// har doim terish xatosi.
    /// </summary>
    private DateTime ValidateManualExpiry(DateTime value)
    {
        var utc = NormalizeExpiry(value);
        var now = _clock.UtcNow;
        if (utc <= now)
            throw new InvalidOperationException("Muddat kelajakdagi sana bo'lishi kerak.");
        if (utc > now.AddYears(10))
            throw new InvalidOperationException("Muddat juda uzoq — sanani tekshiring.");
        return utc;
    }

    public async Task<SaPaymentPreviewDto?> PreviewAsync(
        int marketId, int months, DateTime? expiresAtOverride = null, CancellationToken ct = default)
    {
        if (months < 1) months = 1;
        var market = await _context.Markets.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == marketId && m.IsActive, ct);
        if (market is null) return null;

        var prices = await PriceMapAsync(ct);
        var (computed, anchored) = Extend(market.ExpiresAt, months, _clock.UtcNow, _settings.Current.GraceDays);
        var price = prices.TryGetValue(market.Plan, out var p) ? p : 0m;

        // Qo'lda kiritilgan sana hisoblanganidan ustun turadi; summa esa
        // baribir oylar sonidan olinadi.
        var manual = expiresAtOverride.HasValue;
        var newExpiry = manual ? ValidateManualExpiry(expiresAtOverride!.Value) : computed;

        return new SaPaymentPreviewDto(
            market.ExpiresAt, newExpiry, price * months, market.Plan.ToString(), anchored && !manual, manual);
    }

    public async Task<SaPaymentResultDto?> RecordAsync(
        int marketId, SaRecordPaymentDto dto, Guid superAdminUserId, CancellationToken ct = default)
    {
        if (dto.Months < 1)
            throw new InvalidOperationException("Oylar soni kamida 1 bo'lishi kerak.");

        // Matn → enum: noto'g'ri qiymat DB'ga yetib bormasin.
        var channel = dto.ParseChannel();
        var planOverride = dto.ParsePlan();

        var market = await _context.Markets.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == marketId && m.IsActive, ct);
        if (market is null) return null;

        // Tarif ham o'zgartirilayotgan bo'lsa — narx YANGI tarifdan olinadi,
        // ya'ni «Про ga o'tdi va to'ladi» bitta amalda bajariladi.
        if (planOverride is { } newPlan) market.Plan = newPlan;

        var prices = await PriceMapAsync(ct);
        var price = prices.TryGetValue(market.Plan, out var p) ? p : 0m;

        var nowUtc = _clock.UtcNow;
        // Operator sanani qo'lda kiritgan bo'lsa — aynan o'sha sana yoziladi
        // (oldindan ko'rsatilgan natija bilan bir xil bo'lishi shart).
        var newExpiry = dto.ExpiresAt.HasValue
            ? ValidateManualExpiry(dto.ExpiresAt.Value)
            : Extend(market.ExpiresAt, dto.Months, nowUtc, _settings.Current.GraceDays).NewExpiry;

        var payment = new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Plan = market.Plan,
            AmountUzs = price * dto.Months,
            Months = dto.Months,
            Channel = channel,
            PaidAtUtc = nowUtc,
            PeriodEndUtc = newExpiry,
            AcceptedByUserId = superAdminUserId,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedAt = nowUtc,
        };
        _context.SubscriptionPayments.Add(payment);
        market.ExpiresAt = newExpiry;

        // To'lov qatori va yangi muddat BITTA saqlashda: aks holda pul qabul
        // qilingan, lekin do'kon yopiq qolgan holat yuzaga kelishi mumkin edi.
        await _context.SaveChangesAsync(ct);

        await _audit.LogActionAsync(
            entityType: "SubscriptionPayment",
            entityId: payment.Id,
            action: "PaymentAccepted",
            userId: superAdminUserId,
            payload: new
            {
                MarketId = marketId,
                Plan = market.Plan.ToString(),
                payment.AmountUzs,
                payment.Months,
                Channel = channel.ToString(),
                NewExpiresAt = newExpiry,
            },
            ct);

        return new SaPaymentResultDto(payment.Id, marketId, payment.AmountUzs, newExpiry);
    }

    public async Task<IReadOnlyList<SaPaymentLogDto>> RecentAsync(int take = 10, CancellationToken ct = default)
    {
        return await _context.SubscriptionPayments.AsNoTracking()
            // O'chirilgan do'konning to'lovi ro'yxatda chiqmaydi: konsolning
            // qolgan hamma ro'yxati soft-delete qilingan do'konni yashiradi,
            // bu esa operatorga ro'yxatlardan topib bo'lmaydigan do'kon nomini
            // ko'rsatib turardi.
            .Where(p => p.Market!.IsActive)
            .OrderByDescending(p => p.PaidAtUtc)
            .Take(take)
            .Select(p => new SaPaymentLogDto(
                p.Id,
                p.MarketId,
                p.Market!.Name,
                p.Plan.ToString(),
                p.Channel.ToString(),
                p.AmountUzs,
                p.PaidAtUtc))
            .ToListAsync(ct);
    }

    private async Task<Dictionary<PlanCode, decimal>> PriceMapAsync(CancellationToken ct) =>
        await _context.PlatformPlans.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.PriceUzs, ct);

    /// <summary>
    /// To'rt holat: bloklangan, muddati o'tgan, muddati yaqin, faol. «Yaqin»
    /// alohida — dizayndagi «Скоро срок» tab'i shu bo'yicha filtrlaydi.
    /// </summary>
    private static string StatusOf(bool blocked, DateTime? expiresAt, DateTime nowUtc, DateTime soonUtc) =>
        blocked ? "Blocked"
        : !expiresAt.HasValue ? "Active"
        : expiresAt.Value <= nowUtc ? "Overdue"
        : expiresAt.Value <= soonUtc ? "Soon"
        : "Active";
}
