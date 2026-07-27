using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Панель Buildix» ma'lumotlari.
///
/// <para><b>Tenant izolyatsiyasi.</b> Konsol so'rovi <c>MarketId</c> claim'siz
/// keladi, ya'ni global query-filter o'chiq va bu servis ATAYLAB platforma-keng
/// o'qiydi. Shuning uchun har bir so'rov o'z shartini o'zi yozadi
/// (<c>IgnoreQueryFilters</c> + aniq <c>Where</c>) — filtr "o'zi to'g'rilab
/// qo'yadi" degan taxminga tayanilmaydi.</para>
///
/// <para><b>Vaqt.</b> «Bu oyda ochilgan» — Toshkent kalendari bo'yicha:
/// server UTC'da tursa ham, 1-avgust ertalab ochilgan do'kon iyulga tushib
/// qolmasin.</para>
/// </summary>
public class SuperAdminDashboardService : ISuperAdminDashboardService
{
    /// <summary>Panel ro'yxatlarida nechta qator ko'rsatiladi.</summary>
    private const int ListSize = 6;
    private const int RequestListSize = 3;
    private readonly IAppDbContext _context;
    private readonly ITashkentClock _clock;
    private readonly IPlatformSettingsProvider _settings;

    public SuperAdminDashboardService(
        IAppDbContext context, ITashkentClock clock, IPlatformSettingsProvider settings)
    {
        _context = context;
        _clock = clock;
        _settings = settings;
    }

    public async Task<SaDashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var today = _clock.TodayLocal;
        var monthStartUtc = _clock.LocalDayToUtcRange(new DateTime(today.Year, today.Month, 1)).UtcStart;
        // «Muddati yaqin» chegarasi — sozlamadan (konsolda o'zgartiriladi).
        var soonUtc = nowUtc.AddDays(_settings.Current.SoonThresholdDays);

        // Soft-delete qilingan do'kon (IsActive=false) platformada yo'q
        // hisoblanadi — u DeleteOwner orqali o'chirilgan.
        var live = _context.Markets.IgnoreQueryFilters().AsNoTracking().Where(m => m.IsActive);

        // Har market bo'yicha xodimlar soni va oxirgi faollik — bitta guruhlangan
        // so'rovda. Do'kon ro'yxati bo'ylab alohida so'rov yuborish N+1 bo'lardi.
        var perMarket = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.MarketId != null && !u.IsDeleted && u.IsActive)
            .GroupBy(u => u.MarketId!.Value)
            .Select(g => new { MarketId = g.Key, Users = g.Count(), LastSeen = g.Max(u => u.LastActiveAt) })
            .ToListAsync(cancellationToken);
        var stats = perMarket.ToDictionary(x => x.MarketId);

        var markets = await live
            .Select(m => new { m.Id, m.Name, m.ExpiresAt, m.IsBlocked, m.CreatedAt, m.Plan })
            .ToListAsync(cancellationToken);

        SaDashboardStoreDto ToDto(int id, string name, DateTime? expiresAt, bool blocked)
        {
            stats.TryGetValue(id, out var s);
            var status = blocked
                ? "Blocked"
                : expiresAt.HasValue && expiresAt.Value <= nowUtc ? "Overdue" : "Active";
            return new SaDashboardStoreDto(id, name, expiresAt, s?.Users ?? 0, status, blocked, s?.LastSeen);
        }

        var all = markets.Select(m => ToDto(m.Id, m.Name, m.ExpiresAt, m.IsBlocked)).ToList();

        var overdue = all
            .Where(s => s.Status == "Overdue")
            .OrderBy(s => s.ExpiresAt)
            .ToList();

        // «Muddati yaqin» — hali tugamagan, lekin bir hafta ichida tugaydigan.
        // Bloklangan va allaqachon o'tganlar bu ro'yxatga kirmaydi: ular
        // yuqoridagi qizil blokda.
        var expiringSoon = all
            .Where(s => s.Status == "Active" && s.ExpiresAt.HasValue && s.ExpiresAt.Value <= soonUtc)
            .OrderBy(s => s.ExpiresAt)
            .ToList();

        var newRequests = await _context.RegistrationRequests.AsNoTracking()
            .Where(r => r.Status == RegistrationRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .Take(RequestListSize)
            .Select(r => new SaDashboardRequestDto(r.Id, r.FullName, r.Phone, r.Note, r.CreatedAt))
            .ToListAsync(cancellationToken);

        var pendingCount = await _context.RegistrationRequests.AsNoTracking()
            .CountAsync(r => r.Status == RegistrationRequestStatus.Pending, cancellationToken);

        // MRR — xizmat KO'RSATILAYOTGAN do'konlarning joriy tarif narxlari
        // yig'indisi (kutilayotgan oylik daromad), qabul qilingan to'lovlar
        // yig'indisi emas: dizayndagi «Доход по подпискам · сум/мес» shu ma'noda.
        // Muddati o'tgan va bloklanganlar kirmaydi — ular to'lamayapti.
        // «Подписки» ekranidagi «ожидается» AYNAN shu qoida bilan hisoblanadi.
        var prices = await _context.PlatformPlans.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.PriceUzs, cancellationToken);
        var activeIds = all.Where(s => s.Status == "Active").Select(s => s.MarketId).ToHashSet();
        var mrr = markets
            .Where(m => activeIds.Contains(m.Id))
            .Sum(m => prices.TryGetValue(m.Plan, out var price) ? price : 0m);

        var kpis = new SaDashboardKpisDto(
            ActiveStores: all.Count(s => s.Status == "Active"),
            NewStoresThisMonth: markets.Count(m => m.CreatedAt >= monthStartUtc),
            NewRequests: pendingCount,
            MonthlyRevenueUzs: mrr,
            OverdueStores: overdue.Count);

        return new SaDashboardDto(
            kpis,
            newRequests,
            overdue.Take(ListSize).ToList(),
            expiringSoon.Take(ListSize).ToList(),
            // Panel pastidagi ro'yxat — «последняя активность» bo'yicha, hech
            // qachon kirmaganlar oxirida.
            all.OrderByDescending(s => s.LastActivityUtc ?? DateTime.MinValue).Take(ListSize).ToList());
    }
}
