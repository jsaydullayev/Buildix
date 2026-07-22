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

/// <summary>
/// RegistrationRequestService is a <c>partial</c> class split by concern across
/// several files — the type and every call-site stay unchanged (partials merge
/// at compile time):
///   • <c>RegistrationRequestService.cs</c> (this file) — constructor, fields
///     and the shared helpers (phone / username / subdomain normalisation,
///     request-load, market-name-taken, unique-violation detection).
///   • <c>RegistrationRequestService.Signup.cs</c> — public sign-up + availability.
///   • <c>RegistrationRequestService.Review.cs</c> — SuperAdmin list / approve / reject.
///   • <c>RegistrationRequestService.Owners.cs</c> — owner CRUD + stats.
///   • <c>RegistrationRequestService.Markets.cs</c> — market block / unblock.
/// </summary>
public partial class RegistrationRequestService : IRegistrationRequestService
{
    private readonly IAppDbContext _context;
    private readonly ILogger<RegistrationRequestService> _logger;
    private readonly IAuditLogService _auditLog;
    private readonly IUserTokenEpochStore _tokenEpochStore;

    public RegistrationRequestService(
        IAppDbContext context,
        ILogger<RegistrationRequestService> logger,
        IAuditLogService auditLog,
        IUserTokenEpochStore tokenEpochStore)
    {
        _context = context;
        _logger = logger;
        _auditLog = auditLog;
        _tokenEpochStore = tokenEpochStore;
    }

    /// <summary>
    /// Normalise to strict E.164-like Uzbekistan format `+998XXXXXXXXX` (12 digits
    /// after the plus). Anything else throws — accepting "998..." and "+998..."
    /// as different values would break our partial unique index on Phone.
    /// </summary>
    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new InvalidOperationException("Telefon raqamini kiriting.");

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        // Accept: 9 digits (no country code), 12 digits starting 998, 13 with 00998.
        if (digits.Length == 9) digits = "998" + digits;
        else if (digits.Length == 14 && digits.StartsWith("00998")) digits = digits[2..];
        else if (digits.Length != 12 || !digits.StartsWith("998"))
            throw new InvalidOperationException("Telefon raqami formati noto'g'ri. Misol: +998 90 123 45 67.");

        return "+" + digits;
    }

    /// <summary>
    /// Usernames are stored lowercase + trimmed so that "Sardor", " sardor",
    /// and "sardor" can't coexist as separate accounts (PostgreSQL `=` is
    /// case-sensitive — without this the login query would non-deterministically
    /// pick a row when duplicates exist).
    /// </summary>
    private static string NormalizeUsername(string? username)
    {
        var u = (username ?? string.Empty).Trim().ToLowerInvariant();
        if (u.Length < 3)
            throw new InvalidOperationException("Username kamida 3 ta belgidan iborat bo'lsin.");
        return u;
    }

    // DNS-safe subdomain: lowercase letters/digits/hyphens, must start and end
    // with alphanumeric, 3–63 characters. Empty hyphens, dots, and underscores
    // would break the host header / cert lookup, so we reject them at the edge.
    private static readonly Regex _subdomainPattern = new(
        @"^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$",
        RegexOptions.Compiled);

    private static string ValidateAndNormalizeSubdomain(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        if (s.Length < 3 || s.Length > 63 || !_subdomainPattern.IsMatch(s))
            throw new InvalidOperationException(
                "Subdomain noto'g'ri formatda. Faqat lotin harflari, raqamlar va '-' (3-63 belgi).");
        return s;
    }

    private static string GenerateSubdomain(string username)
    {
        var cleaned = new string(username.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(cleaned)) cleaned = "market";
        return $"{cleaned}{Guid.NewGuid().ToString("N")[..6]}";
    }

    /// <summary>
    /// Case-insensitive existence check for market names. Without this, two
    /// markets named "Sardor Market" and "sardor market" can coexist —
    /// confusing for operators and ambiguous for tenant lookup. EF Core
    /// translates `string.ToLower()` to PostgreSQL `LOWER(...)`, which is
    /// indexable; the unique constraint at the DB level is still
    /// <summary>
    /// Y6 — Re-read a RegistrationRequest inside the surrounding transaction.
    /// On PostgreSQL the query uses <c>SELECT … FOR UPDATE</c> so a parallel
    /// SuperAdmin review on the same row blocks until we commit. On the
    /// EF Core InMemory provider (test suite) raw SQL isn't supported, so
    /// we fall back to a plain query — xmin on the row catches the
    /// concurrent SaveChanges either way, but in tests we get the simpler
    /// "second write fails and retries" semantic instead of a real row lock.
    /// </summary>
    private async Task<RegistrationRequest?> LoadRequestForUpdateAsync(Guid requestId, CancellationToken ct)
    {
        var isPostgres = _context.Database.ProviderName?.Contains("InMemory") == false;
        if (isPostgres)
        {
            return await _context.RegistrationRequests
                .FromSqlInterpolated($"SELECT *, xmin FROM \"RegistrationRequests\" WHERE \"Id\" = {requestId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        return await _context.RegistrationRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
    }

    /// <summary>
    /// case-sensitive but the application-layer check catches the
    /// case-only collision before INSERT.
    /// </summary>
    private async Task<bool> MarketNameTakenAsync(string name, int? excludeMarketId, CancellationToken ct)
    {
        var lowered = name.Trim().ToLowerInvariant();
        if (excludeMarketId.HasValue)
            return await _context.Markets.AnyAsync(
                m => m.Id != excludeMarketId.Value && m.Name.ToLower() == lowered, ct);
        return await _context.Markets.AnyAsync(m => m.Name.ToLower() == lowered, ct);
    }

    // PostgreSQL always includes the SQLSTATE code "23505" in the message for unique violations.
    // Checking the message avoids a direct Npgsql package reference in the Application layer.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.Message?.Contains("23505") == true;
}
