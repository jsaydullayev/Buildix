using System.Text.RegularExpressions;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

// Part of the RegistrationRequestService partial class — owner CRUD + stats
public partial class RegistrationRequestService
{
    public async Task<IEnumerable<OwnerSummaryDto>> ListOwnersAsync(CancellationToken cancellationToken = default)
    {
        // Show every non-deleted Owner — including deactivated ones — so the
        // operator can re-activate them through UpdateOwner. Soft-deleted
        // accounts are still hidden via the global query filter on User.
        // Active owners come first so the common case (managing the live
        // tenant list) stays at the top of the list.
        var owners = await _context.Users
            .AsNoTracking()
            .Include(u => u.Market)
            .Where(u => u.Role == Role.Owner)
            .OrderByDescending(u => u.IsActive)
            .ThenByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

        return owners.Select(u => new OwnerSummaryDto(
            u.Id,
            u.FullName,
            u.Username,
            u.Phone,
            u.IsActive,
            u.MarketId,
            u.Market?.Name,
            u.Market?.IsBlocked ?? false,
            u.CreatedAt
        ));
    }

    public async Task<OwnerDetailDto?> GetOwnerDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // ListOwners hides soft-deleted rows via the query filter — we honour
        // that here for consistency. If we ever need to surface deleted owners
        // (e.g. an "archive" view), use IgnoreQueryFilters on a separate method.
        var owner = await _context.Users
            .AsNoTracking()
            .Include(u => u.Market)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Role == Role.Owner, cancellationToken);
        if (owner == null) return null;

        var marketId = owner.MarketId;
        var stats = marketId.HasValue
            ? await ComputeOwnerStatsAsync(marketId.Value, cancellationToken)
            : new OwnerDetailStatsDto(0, 0, 0, 0, 0m);

        var marketDto = owner.Market is null
            ? null
            : new OwnerDetailMarketDto(
                owner.Market.Id,
                owner.Market.Name,
                owner.Market.Subdomain,
                owner.Market.Description,
                owner.Market.IsActive,
                owner.Market.IsBlocked,
                owner.Market.BlockedAt,
                owner.Market.BlockedReason,
                owner.Market.ExpiresAt,
                owner.Market.CreatedAt);

        return new OwnerDetailDto(
            owner.Id,
            owner.FullName,
            owner.Username,
            owner.Phone,
            owner.IsActive,
            owner.Language.ToString().ToLowerInvariant(),
            owner.CreatedAt,
            marketDto,
            stats);
    }

    private async Task<OwnerDetailStatsDto> ComputeOwnerStatsAsync(int marketId, CancellationToken cancellationToken)
    {
        // Each count is a separate round-trip — fine because this is rare (one
        // page load per market detail view). If this ever becomes hot, fold
        // them into a single raw-SQL query.
        var productsCount = await _context.Products.CountAsync(p => p.MarketId == marketId, cancellationToken);
        var salesCount = await _context.Sales.CountAsync(s => s.MarketId == marketId, cancellationToken);
        var customersCount = await _context.Customers.CountAsync(c => c.MarketId == marketId, cancellationToken);
        // "Cashiers" in the UI means "every non-owner staff member who can ring
        // up a sale" — both Sellers and Admins log into the POS. Counting only
        // Sellers undercounted markets that delegate to an Admin.
        var staffCount = await _context.Users.CountAsync(
            u => u.MarketId == marketId
                 && u.Role != Role.Owner
                 && u.Role != Role.SuperAdmin
                 && u.IsActive,
            cancellationToken);
        var outstandingDebt = await _context.Debts
            .Where(d => d.MarketId == marketId)
            .SumAsync(d => (decimal?)d.RemainingDebt, cancellationToken) ?? 0m;

        return new OwnerDetailStatsDto(productsCount, salesCount, customersCount, staffCount, outstandingDebt);
    }

    public async Task<ApproveRegistrationResultDto> CreateOwnerAsync(CreateOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Trim().Length < 2)
            throw new InvalidOperationException("Ism va familiyani kiriting.");
        var username = NormalizeUsername(dto.Username);
        if (!Buildix.Application.Validation.StrongPasswordAttribute.IsStrong(dto.Password))
            throw new InvalidOperationException("Parol kamida 8 ta belgidan iborat bo'lsin.");
        if (string.IsNullOrWhiteSpace(dto.MarketName) || dto.MarketName.Trim().Length < 3)
            throw new InvalidOperationException("Do'kon nomini kiriting (kamida 3 belgi).");

        var marketName = dto.MarketName.Trim();
        var phone = NormalizePhone(dto.Phone);

        Language language = dto.Language?.ToLowerInvariant() switch
        {
            "ru" => Language.Russian,
            _ => Language.Uzbek
        };

        var subdomain = string.IsNullOrWhiteSpace(dto.Subdomain)
            ? GenerateSubdomain(username)
            : ValidateAndNormalizeSubdomain(dto.Subdomain);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (await _context.Users.AnyAsync(u => u.Username == username, cancellationToken))
                    throw new InvalidOperationException($"'{username}' allaqachon ishlatilgan.");
                if (await MarketNameTakenAsync(marketName, excludeMarketId: null, cancellationToken))
                    throw new InvalidOperationException($"'{marketName}' nomli do'kon allaqachon mavjud.");
                if (await _context.Markets.AnyAsync(m => m.Subdomain == subdomain, cancellationToken))
                    throw new InvalidOperationException($"'{subdomain}' subdomeni allaqachon band.");

                var userId = Guid.NewGuid();
                var user = new User
                {
                    Id = userId,
                    FullName = dto.FullName.Trim(),
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    Phone = phone,
                    Role = Role.Owner,
                    Language = language,
                    IsActive = true,
                    MarketId = null
                };
                await _context.Users.AddAsync(user, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                var market = new Market
                {
                    Name = marketName,
                    Subdomain = subdomain,
                    IsActive = true,
                    ExpiresAt = dto.ExpiresAt,
                    OwnerId = userId
                };
                await _context.Markets.AddAsync(market, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                _context.CashRegisters.Add(new CashRegister
                {
                    Id = Guid.NewGuid(),
                    MarketId = market.Id,
                    CurrentBalance = 0m,
                    LastUpdated = DateTime.UtcNow
                });

                user.MarketId = market.Id;

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Owner manually created: UserId={UserId} MarketId={MarketId} BySuperAdmin={SuperAdminId}",
                    userId, market.Id, superAdminUserId);

                await _auditLog.LogActionAsync(
                    entityType: "Owner",
                    entityId: userId,
                    action: "CreatedManually",
                    userId: superAdminUserId,
                    payload: new { CreatedUserId = userId, CreatedMarketId = market.Id, Username = username, MarketName = market.Name },
                    cancellationToken);

                return new ApproveRegistrationResultDto(
                    Guid.Empty, // No backing request id for a manual create.
                    user.Id,
                    user.Username,
                    market.Id,
                    market.Name);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    "Username, do'kon nomi yoki subdomain allaqachon band. Iltimos, qayta tekshiring.");
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<OwnerDetailDto> UpdateOwnerAsync(Guid userId, UpdateOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || dto.FullName.Trim().Length < 2)
            throw new InvalidOperationException("Ism va familiyani kiriting.");
        if (string.IsNullOrWhiteSpace(dto.MarketName) || dto.MarketName.Trim().Length < 3)
            throw new InvalidOperationException("Do'kon nomini kiriting (kamida 3 belgi).");

        var newMarketName = dto.MarketName.Trim();
        var newSubdomain = string.IsNullOrWhiteSpace(dto.Subdomain)
            ? null
            : ValidateAndNormalizeSubdomain(dto.Subdomain);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var owner = await _context.Users
                    .Include(u => u.Market)
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == Role.Owner, cancellationToken)
                    ?? throw new KeyNotFoundException("Owner topilmadi.");

                // ── Owner fields ────────────────────────────────────────────
                owner.FullName = dto.FullName.Trim();
                if (!string.IsNullOrWhiteSpace(dto.Phone))
                    owner.Phone = NormalizePhone(dto.Phone);
                if (!string.IsNullOrWhiteSpace(dto.Language))
                {
                    owner.Language = dto.Language.ToLowerInvariant() switch
                    {
                        "ru" => Language.Russian,
                        _ => Language.Uzbek
                    };
                }
                if (dto.OwnerActive.HasValue)
                    owner.IsActive = dto.OwnerActive.Value;

                // ── Market fields (only if the Owner has a Market) ──────────
                if (owner.Market is null)
                    throw new InvalidOperationException("Owner uchun do'kon biriktirilmagan.");

                var market = owner.Market;

                // Case-insensitive comparison — matches the create-time check
                // so the operator can fix capitalisation without tripping the
                // uniqueness guard against their own market.
                if (!string.Equals(market.Name, newMarketName, StringComparison.OrdinalIgnoreCase))
                {
                    if (await MarketNameTakenAsync(newMarketName, market.Id, cancellationToken))
                        throw new InvalidOperationException($"'{newMarketName}' nomli do'kon allaqachon mavjud.");
                }
                market.Name = newMarketName;

                if (newSubdomain != null
                    && !string.Equals(market.Subdomain, newSubdomain, StringComparison.Ordinal))
                {
                    if (await _context.Markets.AnyAsync(m => m.Id != market.Id && m.Subdomain == newSubdomain, cancellationToken))
                        throw new InvalidOperationException($"'{newSubdomain}' subdomeni allaqachon band.");
                    market.Subdomain = newSubdomain;
                }

                if (dto.Description != null) market.Description = dto.Description.Trim();
                if (dto.MarketActive.HasValue) market.IsActive = dto.MarketActive.Value;
                if (dto.ExpiresAt.HasValue) market.ExpiresAt = dto.ExpiresAt.Value;

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Owner updated: UserId={UserId} MarketId={MarketId} BySuperAdmin={SuperAdminId}",
                    userId, market.Id, superAdminUserId);

                await _auditLog.LogActionAsync(
                    entityType: "Owner",
                    entityId: userId,
                    action: "Updated",
                    userId: superAdminUserId,
                    payload: new { MarketId = market.Id, MarketName = market.Name, OwnerActive = owner.IsActive, MarketActive = market.IsActive },
                    cancellationToken);

                // Reload through the detail path so the response includes stats.
                var refreshed = await GetOwnerDetailAsync(userId, cancellationToken);
                return refreshed!;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                throw new InvalidOperationException(
                    "Do'kon nomi yoki subdomain allaqachon band. Iltimos, qayta tekshiring.");
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<bool> DeleteOwnerAsync(Guid userId, DeleteOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException("O'chirish sababini kiriting.");
        if (string.IsNullOrWhiteSpace(dto.ConfirmMarketName))
            throw new InvalidOperationException("Do'kon nomini tasdiqlash uchun kiriting.");

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var owner = await _context.Users
                    .Include(u => u.Market)
                    .FirstOrDefaultAsync(u => u.Id == userId && u.Role == Role.Owner, cancellationToken);
                if (owner == null) return false;

                // Typed-confirmation guard — mirrors the destructive-action dialog
                // so a fat-fingered DELETE can't take out the wrong tenant.
                if (owner.Market == null ||
                    !string.Equals(owner.Market.Name.Trim(), dto.ConfirmMarketName.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Tasdiqlash do'kon nomi mos kelmadi.");
                }

                // Soft-delete: User goes to IsDeleted (hidden by the global query
                // filter), Market is deactivated (still readable in the DB for
                // forensics but no Tenant resolves to it). Historical sales,
                // products, debts, etc. are intentionally left intact.
                owner.IsActive = false;
                owner.IsDeleted = true;
                owner.DeletedAt = DateTime.UtcNow;
                owner.Market.IsActive = false;

                // Cascade-deactivate every non-Owner user in this market so they
                // can't log in either. Previously this only touched Sellers —
                // any Admin under the market would have remained reachable.
                var staff = await _context.Users
                    .Where(u => u.MarketId == owner.Market.Id
                                && u.Role != Role.Owner
                                && u.Role != Role.SuperAdmin
                                && u.IsActive)
                    .ToListAsync(cancellationToken);
                foreach (var member in staff)
                    member.IsActive = false;

                // IsActive=false ni o'rnatishning O'ZI sessiyani o'ldirmaydi:
                //  • allaqachon berilgan access token o'zining TTL'i (30 daqiqagacha)
                //    tugagunicha ishlayveradi — har so'rovda IsActive tekshirilmaydi;
                //  • refresh tokenlar esa DB'da tirik qoladi.
                // Ya'ni to'lamagani uchun to'xtatilgan do'kon xodimlari yana yarim soat
                // savdo qilishi, chek berishi va kassadan pul yechishi mumkin edi.
                // Shuning uchun: refresh oilasini kuydiramiz + token epoxasini
                // stamplaymiz (iat < epoch bo'lgan access token darhol rad etiladi).
                var invalidatedAt = DateTime.UtcNow;
                var victims = staff.Append(owner).ToList();
                foreach (var u in victims)
                {
                    u.TokensInvalidBeforeUtc = invalidatedAt;

                    var live = await _context.RefreshTokens
                        .Where(r => r.UserId == u.Id && !r.IsRevoked)
                        .ToListAsync(cancellationToken);
                    foreach (var rt in live)
                    {
                        rt.IsRevoked = true;
                        rt.RevokedAt = invalidatedAt;
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                // Commit'dan KEYIN keshni yangilaymiz (rollback bo'lsa kesh iflos
                // bo'lib qolmasin). Bu O(1) hot-path lookup manbai.
                foreach (var u in victims)
                    _tokenEpochStore.Publish(u.Id, invalidatedAt);

                _logger.LogWarning(
                    "Owner soft-deleted: UserId={UserId} MarketId={MarketId} BySuperAdmin={SuperAdminId} Reason={Reason}",
                    userId, owner.Market.Id, superAdminUserId, dto.Reason);

                await _auditLog.LogActionAsync(
                    entityType: "Owner",
                    entityId: userId,
                    action: "SoftDeleted",
                    userId: superAdminUserId,
                    payload: new
                    {
                        MarketId = owner.Market.Id,
                        MarketName = owner.Market.Name,
                        Reason = dto.Reason.Trim(),
                        StaffDeactivated = staff.Count
                    },
                    cancellationToken);

                return true;
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
