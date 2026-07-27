using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Konsolning «Магазины» ekrani — ro'yxat va detal.
///
/// <para><b>Tenant izolyatsiyasi.</b> SuperAdmin so'rovida <c>MarketId</c>
/// claim yo'q → global query-filter o'chiq. Bu servis ATAYLAB platforma-keng
/// o'qiydi, shuning uchun har bir so'rov o'z shartini o'zi yozadi.</para>
/// </summary>
public class SuperAdminStoreService : ISuperAdminStoreService
{
    private readonly IAppDbContext _context;
    private readonly ITashkentClock _clock;

    public SuperAdminStoreService(IAppDbContext context, ITashkentClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SaStoreRowDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        // Soft-delete qilingan do'kon (IsActive=false) ro'yxatda yo'q — u
        // DeleteOwner orqali o'chirilgan va platformada mavjud emas.
        var markets = await _context.Markets.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.IsActive)
            .Select(m => new
            {
                m.Id, m.Name, m.City, m.Subdomain, m.CreatedAt, m.ExpiresAt, m.IsBlocked, m.OwnerId, m.Plan,
            })
            .ToListAsync(cancellationToken);
        if (markets.Count == 0) return Array.Empty<SaStoreRowDto>();

        var marketIds = markets.Select(m => m.Id).ToList();

        // Xodimlar soni + oxirgi faollik — bitta guruhlangan so'rovda (do'kon
        // boshiga alohida so'rov N+1 bo'lardi).
        var stats = (await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.MarketId != null && marketIds.Contains(u.MarketId.Value)
                            && !u.IsDeleted && u.IsActive)
                .GroupBy(u => u.MarketId!.Value)
                .Select(g => new { MarketId = g.Key, Users = g.Count(), LastSeen = g.Max(u => u.LastActiveAt) })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.MarketId);

        var ownerIds = markets.Select(m => m.OwnerId).ToList();
        var owners = (await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => ownerIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Phone })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        return markets
            .Select(m =>
            {
                stats.TryGetValue(m.Id, out var s);
                owners.TryGetValue(m.OwnerId, out var o);
                return new SaStoreRowDto(
                    m.Id,
                    m.Name,
                    m.City,
                    m.Subdomain,
                    m.CreatedAt,
                    m.OwnerId,
                    o?.FullName ?? "—",
                    o?.Phone,
                    m.Plan.ToString(),
                    m.ExpiresAt,
                    s?.Users ?? 0,
                    StatusOf(m.IsBlocked, m.ExpiresAt, nowUtc),
                    m.IsBlocked,
                    s?.LastSeen);
            })
            .OrderBy(r => r.Name)
            .ToList();
    }

    public async Task<SaStoreDetailDto?> GetAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var m = await _context.Markets.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == marketId && x.IsActive, cancellationToken);
        if (m is null) return null;

        var owner = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == m.OwnerId)
            .Select(u => new { u.FullName, u.Phone })
            .FirstOrDefaultAsync(cancellationToken);

        var users = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(u => u.MarketId == marketId && !u.IsDeleted && u.IsActive, cancellationToken);

        var lastSeen = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.MarketId == marketId && !u.IsDeleted)
            .MaxAsync(u => (DateTime?)u.LastActiveAt, cancellationToken);

        // «Чеков за месяц» — mahalliy (Toshkent) oy boshidan. Bekor qilingan va
        // hali yakunlanmagan qoralamalar sanalmaydi: dizayndagi raqam «shuncha
        // savdo bo'ldi» degani.
        var today = _clock.TodayLocal;
        var monthStartUtc = _clock.LocalDayToUtcRange(new DateTime(today.Year, today.Month, 1)).UtcStart;
        var checks = await _context.Sales.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(s => s.MarketId == marketId && !s.IsDeleted
                             && s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled
                             && s.CreatedAt >= monthStartUtc, cancellationToken);

        var debt = await _context.Debts.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.MarketId == marketId)
            .SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;

        var row = new SaStoreRowDto(
            m.Id, m.Name, m.City, m.Subdomain, m.CreatedAt, m.OwnerId,
            owner?.FullName ?? "—", owner?.Phone,
            m.Plan.ToString(),
            m.ExpiresAt, users,
            StatusOf(m.IsBlocked, m.ExpiresAt, _clock.UtcNow),
            m.IsBlocked, lastSeen);

        // «История оплат» — kartochkadagi oxirgi to'lovlar (S3'dan beri real).
        var payments = await _context.SubscriptionPayments.AsNoTracking()
            .Where(p => p.MarketId == marketId)
            .OrderByDescending(p => p.PaidAtUtc)
            .Take(10)
            .Select(p => new SaStorePaymentDto(p.PaidAtUtc, p.Channel.ToString(), p.AmountUzs))
            .ToListAsync(cancellationToken);

        return new SaStoreDetailDto(
            row,
            m.BlockedAt,
            m.BlockedReason,
            new SaStoreStatsDto(users, checks, lastSeen, debt),
            payments);
    }

    /// <summary>
    /// Uch holat uchta mustaqil manbadan: qo'lda blok, obuna muddati, qolgani
    /// faol. Ular ARALASHMAYDI — bloklangan do'kon «muddati o'tgan» deb
    /// ko'rsatilsa, operator uni to'lov bilan hal qilinadi deb o'ylardi.
    /// </summary>
    private static string StatusOf(bool blocked, DateTime? expiresAt, DateTime nowUtc) =>
        blocked ? "Blocked"
        : expiresAt.HasValue && expiresAt.Value <= nowUtc ? "Overdue"
        : "Active";
}
