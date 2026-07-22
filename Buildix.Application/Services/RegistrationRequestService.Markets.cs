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

// Part of the RegistrationRequestService partial class — market block / unblock moderation
public partial class RegistrationRequestService
{
    public async Task<MarketBlockStatusDto> BlockMarketAsync(int marketId, BlockMarketDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 3)
            throw new InvalidOperationException("Bloklash sababini kiriting (kamida 3 belgi).");

        var market = await _context.Markets.FirstOrDefaultAsync(m => m.Id == marketId, cancellationToken)
            ?? throw new KeyNotFoundException("Do'kon topilmadi.");

        // Idempotent — re-blocking refreshes the reason/timestamp/actor but
        // doesn't error. Operators rely on this when escalating from "warning"
        // to "blocked" reasons after a follow-up.
        market.IsBlocked = true;
        market.BlockedAt = DateTime.UtcNow;
        market.BlockedReason = dto.Reason.Trim();
        market.BlockedByUserId = superAdminUserId;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Market blocked: MarketId={MarketId} MarketName={MarketName} BySuperAdmin={SuperAdminId} Reason={Reason}",
            market.Id, market.Name, superAdminUserId, market.BlockedReason);

        await _auditLog.LogActionAsync(
            entityType: AuditEntityTypes.Market,
            entityId: Guid.Empty,                       // Market.Id is an int, not a Guid.
            action: AuditActions.Block,
            userId: superAdminUserId,
            payload: new { MarketId = market.Id, MarketName = market.Name, market.BlockedReason },
            cancellationToken);

        return new MarketBlockStatusDto(market.Id, market.Name, true, market.BlockedAt, market.BlockedReason);
    }

    public async Task<MarketBlockStatusDto> UnblockMarketAsync(int marketId, Guid superAdminUserId, CancellationToken cancellationToken = default)
    {
        var market = await _context.Markets.FirstOrDefaultAsync(m => m.Id == marketId, cancellationToken)
            ?? throw new KeyNotFoundException("Do'kon topilmadi.");

        var wasBlocked = market.IsBlocked;
        market.IsBlocked = false;
        market.BlockedAt = null;
        market.BlockedReason = null;
        market.BlockedByUserId = null;

        await _context.SaveChangesAsync(cancellationToken);

        if (wasBlocked)
        {
            _logger.LogInformation(
                "Market unblocked: MarketId={MarketId} MarketName={MarketName} BySuperAdmin={SuperAdminId}",
                market.Id, market.Name, superAdminUserId);

            await _auditLog.LogActionAsync(
                entityType: AuditEntityTypes.Market,
                entityId: Guid.Empty,
                action: AuditActions.Unblock,
                userId: superAdminUserId,
                payload: new { MarketId = market.Id, MarketName = market.Name },
                cancellationToken);
        }

        return new MarketBlockStatusDto(market.Id, market.Name, false, null, null);
    }
}
