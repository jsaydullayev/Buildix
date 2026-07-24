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

// Part of the RegistrationRequestService partial class — SuperAdmin request review: list / approve / reject
public partial class RegistrationRequestService
{
    public async Task<IEnumerable<RegistrationRequestDto>> ListAsync(RegistrationRequestStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _context.RegistrationRequests
            .AsNoTracking()
            .Include(r => r.ProcessedByUser)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        query = query.OrderByDescending(r => r.CreatedAt);

        var items = await query.ToListAsync(cancellationToken);

        return items.Select(r => new RegistrationRequestDto(
            r.Id,
            r.FullName,
            r.Phone,
            r.Status.ToString(),
            r.CreatedAt,
            r.ProcessedAt,
            r.ProcessedByUser?.FullName,
            r.CreatedUserId,
            r.CreatedMarketId,
            r.RejectReason
        ));
    }

    public async Task<ApproveRegistrationResultDto> ApproveAsync(Guid requestId, ApproveRegistrationRequestDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        var username = NormalizeUsername(dto.Username);
        if (!Buildix.Application.Validation.StrongPasswordAttribute.IsStrong(dto.Password))
            throw new InvalidOperationException("Parol kamida 8 ta belgidan iborat bo'lsin.");
        if (string.IsNullOrWhiteSpace(dto.MarketName) || dto.MarketName.Trim().Length < 3)
            throw new InvalidOperationException("Do'kon nomini kiriting (kamida 3 belgi).");
        var marketName = dto.MarketName.Trim();

        Language language = LanguageCodes.FromCode(dto.Language) ?? Language.Uzbek;

        var subdomain = string.IsNullOrWhiteSpace(dto.Subdomain)
            ? GenerateSubdomain(username)
            : ValidateAndNormalizeSubdomain(dto.Subdomain);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Y6 — Re-read INSIDE the transaction with a row lock so a
                // concurrent SuperAdmin can't pass the same Pending check.
                // FOR UPDATE is PostgreSQL-only; the InMemory test provider
                // falls back to a plain query. xmin on the row still catches
                // concurrent SaveChanges in both providers, so correctness is
                // preserved.
                var request = await LoadRequestForUpdateAsync(requestId, cancellationToken)
                    ?? throw new KeyNotFoundException("So'rov topilmadi.");

                if (request.Status != RegistrationRequestStatus.Pending)
                    throw new InvalidOperationException($"So'rov allaqachon ko'rib chiqilgan ({request.Status}).");

                // Belt-and-braces unique checks. The case-insensitive lookups
                // catch operator typos ("Sardor" vs "sardor"); the DB unique
                // constraint is the final source of truth — see the catch
                // block below.
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
                    FullName = request.FullName,
                    Username = username,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                    Phone = request.Phone,
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

                request.Status = RegistrationRequestStatus.Approved;
                request.ProcessedAt = DateTime.UtcNow;
                request.ProcessedByUserId = superAdminUserId;
                request.CreatedUserId = userId;
                request.CreatedMarketId = market.Id;

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Registration approved: RequestId={RequestId} UserId={UserId} MarketId={MarketId} BySuperAdmin={SuperAdminId}",
                    requestId, userId, market.Id, superAdminUserId);

                await _auditLog.LogActionAsync(
                    entityType: "RegistrationRequest",
                    entityId: requestId,
                    action: "Approved",
                    userId: superAdminUserId,
                    payload: new { CreatedUserId = userId, CreatedMarketId = market.Id, Username = username, MarketName = market.Name },
                    cancellationToken);

                return new ApproveRegistrationResultDto(
                    request.Id,
                    user.Id,
                    user.Username,
                    market.Id,
                    market.Name
                );
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Race: a parallel approve/create slipped through between our
                // AnyAsync check and the INSERT. Convert to a clean 400 so the
                // operator sees an actionable message instead of a 500.
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

    public async Task<bool> RejectAsync(Guid requestId, string reason, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Rad etish sababini kiriting.");

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Y6 — FOR UPDATE on PostgreSQL, fall back to a plain read on
                // the InMemory test provider. See ApproveAsync for the full
                // rationale.
                var request = await LoadRequestForUpdateAsync(requestId, cancellationToken);
                if (request == null) return false;

                if (request.Status == RegistrationRequestStatus.Rejected) return true; // idempotent
                if (request.Status != RegistrationRequestStatus.Pending)
                    throw new InvalidOperationException($"So'rov allaqachon {request.Status} holatida.");

                request.Status = RegistrationRequestStatus.Rejected;
                request.ProcessedAt = DateTime.UtcNow;
                request.ProcessedByUserId = superAdminUserId;
                request.RejectReason = reason.Trim();

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Registration rejected: RequestId={RequestId} BySuperAdmin={SuperAdminId}",
                    requestId, superAdminUserId);

                await _auditLog.LogActionAsync(
                    entityType: "RegistrationRequest",
                    entityId: requestId,
                    action: "Rejected",
                    userId: superAdminUserId,
                    payload: new { Reason = request.RejectReason },
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
