using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Well-known <see cref="Result.Code"/> values for the public sign-up flow.
/// A <see cref="Validation"/> failure is safe to show the visitor; every other
/// failure is suppressed behind the generic acknowledgement so the pending
/// queue can't be probed for existing phone numbers.
/// </summary>
public static class RegistrationSubmitCodes
{
    public const string Validation = "VALIDATION";
}

public interface IRegistrationRequestService
{
    /// <summary>
    /// Public sign-up — anonymous submission. On success the request is queued.
    /// On failure the <see cref="Result"/> carries the reason and a Code:
    /// <see cref="RegistrationSubmitCodes.Validation"/> for user-facing formatting
    /// errors, anything else for suppressed reasons (duplicate phone, …).
    /// </summary>
    Task<Result> SubmitAsync(SubmitRegistrationRequestDto dto, CancellationToken cancellationToken = default);

    /// <summary>SuperAdmin list. If <paramref name="status"/> is null, returns all statuses.</summary>
    Task<IEnumerable<RegistrationRequestDto>> ListAsync(RegistrationRequestStatus? status, CancellationToken cancellationToken = default);

    /// <summary>Active Owner users across all markets — SuperAdmin only.</summary>
    Task<IEnumerable<OwnerSummaryDto>> ListOwnersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a request — atomically creates a new Owner user, a new dedicated
    /// Market, and a CashRegister scoped to that market. Returns credentials info
    /// so the SuperAdmin can hand them to the owner.
    /// </summary>
    Task<ApproveRegistrationResultDto> ApproveAsync(Guid requestId, ApproveRegistrationRequestDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>Reject a request with a reason. Idempotent on a request already rejected.</summary>
    Task<bool> RejectAsync(Guid requestId, string reason, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Real-time uniqueness check for the approve form. Any combination of fields
    /// may be supplied; each comes back as <c>true</c> (free), <c>false</c> (taken),
    /// or <c>null</c> (not asked). When a market name is supplied without a
    /// subdomain, returns a generated suggestion so the UI can preview it.
    /// </summary>
    Task<CheckAvailabilityResultDto> CheckAvailabilityAsync(
        string? username,
        string? marketName,
        string? subdomain,
        CancellationToken cancellationToken = default);

    /// <summary>Full owner detail (Owner + Market + live stats). Returns null if the user is not an Owner or has been deleted.</summary>
    Task<OwnerDetailDto?> GetOwnerDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manual create — same shape as <see cref="ApproveAsync"/> but without a backing
    /// registration request. Used when the SuperAdmin onboards a tenant out-of-band.
    /// </summary>
    Task<ApproveRegistrationResultDto> CreateOwnerAsync(CreateOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>Update mutable Owner+Market fields. Username and password are not editable here.</summary>
    Task<OwnerDetailDto> UpdateOwnerAsync(Guid userId, UpdateOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete an Owner and deactivate their Market. Historical data
    /// (sales, products, debts) is preserved for audit; the market simply
    /// becomes unreachable. Returns false if the user is not found.
    /// </summary>
    Task<bool> DeleteOwnerAsync(Guid userId, DeleteOwnerDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Administratively block a market (non-payment, ToS violation, etc.).
    /// Login and tenant-resolution will reject the entire market until
    /// <see cref="UnblockMarketAsync"/> is called. Idempotent: blocking an
    /// already-blocked market updates the reason and timestamp.
    /// </summary>
    Task<MarketBlockStatusDto> BlockMarketAsync(int marketId, BlockMarketDto dto, Guid superAdminUserId, CancellationToken cancellationToken = default);

    /// <summary>Restore a previously blocked market. Idempotent on already-unblocked markets.</summary>
    Task<MarketBlockStatusDto> UnblockMarketAsync(int marketId, Guid superAdminUserId, CancellationToken cancellationToken = default);
}
