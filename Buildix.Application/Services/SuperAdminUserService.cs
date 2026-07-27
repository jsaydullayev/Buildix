using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Пользователи платформы» — barcha do'konlar xodimlari bitta ro'yxatda.
///
/// <para><b>Chegara.</b> SuperAdmin bu yerdan faqat PLATFORMAGA kirishni
/// boshqaradi: parolni tiklaydi va hisobni yoqadi/o'chiradi. Rollar va do'kon
/// ichidagi ruxsatlar egasi/administratori ishi (dizayn izohi ham shuni
/// aytadi) — shuning uchun bu servisda rol o'zgartirish umuman yo'q.</para>
///
/// <para><b>Sessiyalar.</b> Parol tiklash yoki hisobni o'chirish — xavfsizlik
/// amali, ya'ni faqat DB'dagi qatorni o'zgartirish yetarli emas: refresh
/// tokenlar bekor qilinadi va <c>TokensInvalidBeforeUtc</c> stamp qo'yiladi,
/// aks holda o'g'irlangan access token yana 30 daqiqa ishlayverardi.</para>
/// </summary>
public class SuperAdminUserService : ISuperAdminUserService
{
    private const int MaxPageSize = 100;

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserTokenEpochStore _tokenEpochStore;
    private readonly IAuditLogService _audit;

    public SuperAdminUserService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        IUserTokenEpochStore tokenEpochStore,
        IAuditLogService audit)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _tokenEpochStore = tokenEpochStore;
        _audit = audit;
    }

    public async Task<PagedResult<SaUserRowDto>> ListAsync(
        string? role, int? marketId, string? search, int page, int size,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (size < 1) size = 20;
        if (size > MaxPageSize) size = MaxPageSize;

        // Filtrlash va sahifalash SERVER tomonda: platformada foydalanuvchilar
        // soni do'konlar bilan birga o'sadi, klient filtri esa butun bazani
        // tortib olishni talab qilardi.
        var query = _context.Users.IgnoreQueryFilters().AsNoTracking()
            // Do'konga tegishli xodimlar; SuperAdmin(lar) o'zi ro'yxatda
            // ko'rinmaydi — konsol do'kon xodimlarini boshqaradi.
            .Where(u => !u.IsDeleted && u.MarketId != null && u.Role != Role.SuperAdmin);

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<Role>(role, ignoreCase: true, out var r))
            query = query.Where(u => u.Role == r);

        if (marketId is { } mid)
            query = query.Where(u => u.MarketId == mid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLower();
            var digits = new string(q.Where(char.IsDigit).ToArray());
            query = query.Where(u =>
                u.FullName.ToLower().Contains(q)
                || u.Username.ToLower().Contains(q)
                || (digits.Length > 0 && u.Phone != null && u.Phone.Contains(digits)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(u => u.MarketId).ThenBy(u => u.Role).ThenBy(u => u.FullName)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(u => new SaUserRowDto(
                u.Id, u.FullName, u.Username, u.Phone, u.Role.ToString(),
                u.MarketId, u.Market!.Name, u.LastActiveAt, u.IsActive))
            .ToListAsync(ct);

        return PagedResult<SaUserRowDto>.From(rows, page, size, total);
    }

    public async Task<bool> ResetPasswordAsync(
        Guid userId, string newPassword, Guid superAdminUserId, CancellationToken ct = default)
    {
        if (!Validation.StrongPasswordAttribute.IsStrong(newPassword))
            throw new InvalidOperationException("Parol kamida 8 ta belgidan iborat bo'lsin.");

        var user = await LoadManageableAsync(userId, ct);
        if (user is null) return false;

        var utcNow = DateTime.UtcNow;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        // Eski parol SHU ZAHOTI ishlamay qolishi kerak — «parolni tiklash»
        // odatda aynan o'g'irlangan kirishni uzish uchun bosiladi.
        await _unitOfWork.RefreshTokens.RevokeAllForUserAsync(user.Id, ct);
        user.TokensInvalidBeforeUtc = utcNow;

        await _context.SaveChangesAsync(ct);
        // Kesh commit'dan KEYIN — rollback bo'lgan o'zgarish keshda qolmasin.
        _tokenEpochStore.Publish(user.Id, utcNow);

        await _audit.LogActionAsync(
            entityType: "User", entityId: user.Id, action: "PasswordResetBySuperAdmin",
            userId: superAdminUserId, payload: new { user.Username, user.MarketId }, ct);
        return true;
    }

    public async Task<bool> SetActiveAsync(
        Guid userId, bool active, Guid superAdminUserId, CancellationToken ct = default)
    {
        var user = await LoadManageableAsync(userId, ct);
        if (user is null) return false;
        if (user.IsActive == active) return true; // idempotent

        user.IsActive = active;
        if (!active)
        {
            // Bloklash — hozirning o'zida kuchga kirsin.
            var utcNow = DateTime.UtcNow;
            await _unitOfWork.RefreshTokens.RevokeAllForUserAsync(user.Id, ct);
            user.TokensInvalidBeforeUtc = utcNow;
            await _context.SaveChangesAsync(ct);
            _tokenEpochStore.Publish(user.Id, utcNow);
        }
        else
        {
            await _context.SaveChangesAsync(ct);
        }

        await _audit.LogActionAsync(
            entityType: "User", entityId: user.Id,
            action: active ? "ActivatedBySuperAdmin" : "BlockedBySuperAdmin",
            userId: superAdminUserId, payload: new { user.Username, user.MarketId }, ct);
        return true;
    }

    /// <summary>
    /// Konsoldan boshqarish mumkin bo'lgan foydalanuvchi: o'chirilmagan va
    /// do'konga tegishli. Boshqa SuperAdmin bu yerdan tegilmaydi — platforma
    /// administratorlari bir-birini konsol orqali qulflab qo'ymasin.
    /// </summary>
    private async Task<Domain.Entities.User?> LoadManageableAsync(Guid userId, CancellationToken ct)
    {
        var user = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null) return null;
        if (user.Role == Role.SuperAdmin || user.MarketId is null)
            throw new InvalidOperationException("Platforma administratori konsol orqali o'zgartirilmaydi.");
        return user;
    }
}
