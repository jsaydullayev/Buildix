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

// Part of the RegistrationRequestService partial class — public sign-up + availability checks
public partial class RegistrationRequestService
{
    public async Task<Result> SubmitAsync(SubmitRegistrationRequestDto dto, CancellationToken cancellationToken = default)
    {
        // DTO record has a default ctor that maps unset fields to string.Empty,
        // but System.Text.Json will preserve an explicit `null` in the payload —
        // null-coalesce so the validation message wins instead of an NRE.
        var fullName = dto.FullName ?? string.Empty;
        var rawPhone = dto.Phone ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2)
            return Result.Failure("Ism va familiyani kiriting.", RegistrationSubmitCodes.Validation);

        string phone;
        try
        {
            phone = NormalizePhone(rawPhone);
        }
        catch (InvalidOperationException ex)
        {
            // NormalizePhone only ever throws user-facing formatting messages
            // ("... kiriting.", "... format ..."); surface them to the visitor.
            return Result.Failure(ex.Message, RegistrationSubmitCodes.Validation);
        }

        var request = new RegistrationRequest
        {
            Id = Guid.NewGuid(),
            FullName = fullName.Trim(),
            Phone = phone,
            Status = RegistrationRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.RegistrationRequests.Add(request);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race: another submission with the same phone won the unique index.
            // We swallow the conflict deliberately — a non-VALIDATION failure is
            // mapped by the public controller to the generic "Adminga yubordik"
            // so a stranger can't probe whether a phone is in the queue.
            _logger.LogInformation("Duplicate pending submission for {Phone} rejected by unique index.", phone);
            return Result.Failure("DUPLICATE_PENDING");
        }

        _logger.LogInformation("Registration request submitted: {RequestId} for {Phone}", request.Id, phone);
        return Result.Success();
    }

    public async Task<CheckAvailabilityResultDto> CheckAvailabilityAsync(
        string? username,
        string? marketName,
        string? subdomain,
        CancellationToken cancellationToken = default)
    {
        // Each field is queried independently — null means "the caller didn't ask".
        // Inputs are normalised the same way Approve/Create do so the live check
        // matches what the server would actually save (e.g. "Sardor" → "sardor").
        bool? usernameAvailable = null;
        bool? marketNameAvailable = null;
        bool? subdomainAvailable = null;
        string? suggested = null;

        var u = username?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(u) && u.Length >= 3)
        {
            usernameAvailable = !await _context.Users.AnyAsync(x => x.Username == u, cancellationToken);
        }

        var mRaw = marketName?.Trim();
        if (!string.IsNullOrEmpty(mRaw) && mRaw.Length >= 3)
        {
            // Case-insensitive — operator's "Sardor Market" collides with "sardor market".
            marketNameAvailable = !await MarketNameTakenAsync(mRaw, excludeMarketId: null, cancellationToken);
        }

        var s = subdomain?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(s))
        {
            // Pre-validate format too so the UI flags "my market!" as taken-ish
            // (we don't expose the validation error here — UI handles its own
            // regex feedback — but a bad subdomain can never be available).
            if (!_subdomainPattern.IsMatch(s) || s.Length < 3 || s.Length > 63)
                subdomainAvailable = false;
            else
                subdomainAvailable = !await _context.Markets.AnyAsync(x => x.Subdomain == s, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(u))
        {
            // Supplied a username but no subdomain — offer the auto-generated one
            // so the UI can show a live preview without the user having to type.
            suggested = GenerateSubdomain(u);
        }

        return new CheckAvailabilityResultDto(
            usernameAvailable,
            marketNameAvailable,
            subdomainAvailable,
            suggested);
    }
}
