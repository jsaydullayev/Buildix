using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// «Настройки платформы» — tarif narxlari, blok qoidalari, Telegram
/// bildirishnomalari va qo'llab-quvvatlash kontaktlari.
///
/// <para>Yozuvdan keyin kesh MAJBURIY yangilanadi
/// (<see cref="IPlatformSettingsProvider.ReloadAsync"/>): obuna eshigi shu
/// keshdan o'qiladi, ya'ni yangilanmasa operator sozlamani o'zgartirgan-u,
/// platforma eski qoida bo'yicha ishlayverardi.</para>
/// </summary>
public class SuperAdminSettingsService : ISuperAdminSettingsService
{
    private readonly IAppDbContext _context;
    private readonly IPlatformSettingsProvider _provider;
    private readonly IAuditLogService _audit;

    public SuperAdminSettingsService(
        IAppDbContext context, IPlatformSettingsProvider provider, IAuditLogService audit)
    {
        _context = context;
        _provider = provider;
        _audit = audit;
    }

    public async Task<SaSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var row = await LoadAsync(ct);
        var plans = await _context.PlatformPlans.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

        return new SaSettingsDto(
            plans.Select(p => new SaPlanPriceDto(p.Code.ToString(), p.PriceUzs, p.MaxUsers, p.MaxPoints)).ToList(),
            row.GraceDays,
            row.WarnOnOverdue,
            row.RestrictAfterGrace,
            row.FullBlockAfterDays,
            row.SoonThresholdDays,
            row.NotifyExpiring,
            row.NotifyBlocked,
            row.ExpiryReminderDays,
            row.SupportPhone,
            row.SupportTelegram,
            row.SupportEmail);
    }

    public async Task<SaSettingsDto> UpdateAsync(SaUpdateSettingsDto dto, Guid superAdminUserId, CancellationToken ct = default)
    {
        if (dto.GraceDays is < 0 or > 90)
            throw new InvalidOperationException("Otsrochka 0 dan 90 kungacha bo'lishi mumkin.");
        if (dto.FullBlockAfterDays is < 0 or > 365)
            throw new InvalidOperationException("To'liq blok muddati 0 dan 365 kungacha bo'lishi mumkin.");
        // Otsrochka to'liq blokdan uzun bo'lsa, «faqat ko'rish» bosqichi
        // umuman yuzaga kelmasdi — sozlama o'zini o'zi inkor qilardi.
        if (dto.FullBlockAfterDays > 0 && dto.FullBlockAfterDays <= dto.GraceDays)
            throw new InvalidOperationException("To'liq blok muddati otsrochkadan uzun bo'lishi kerak.");

        var row = await LoadAsync(ct);
        row.GraceDays = dto.GraceDays;
        row.WarnOnOverdue = dto.WarnOnOverdue;
        row.RestrictAfterGrace = dto.RestrictAfterGrace;
        row.FullBlockAfterDays = dto.FullBlockAfterDays;
        row.SoonThresholdDays = dto.SoonThresholdDays is >= 1 and <= 60 ? dto.SoonThresholdDays : 7;
        row.NotifyExpiring = dto.NotifyExpiring;
        row.NotifyBlocked = dto.NotifyBlocked;
        row.ExpiryReminderDays = dto.ExpiryReminderDays is >= 1 and <= 30 ? dto.ExpiryReminderDays : 3;
        row.SupportPhone = Trim(dto.SupportPhone);
        row.SupportTelegram = Trim(dto.SupportTelegram);
        row.SupportEmail = Trim(dto.SupportEmail);
        row.UpdatedAtUtc = DateTime.UtcNow;

        // Narxlar — kelajakdagi to'lovlar uchun. O'tgan to'lovlar o'z summasini
        // saqlaydi (SubscriptionPayment), shuning uchun tarix o'zgarmaydi.
        foreach (var p in dto.Plans ?? Array.Empty<SaPlanPriceDto>())
        {
            if (!Enum.TryParse<PlanCode>(p.Code, ignoreCase: true, out var code)) continue;
            if (p.PriceUzs < 0) throw new InvalidOperationException("Tarif narxi manfiy bo'lmasin.");

            var plan = await _context.PlatformPlans.FirstOrDefaultAsync(x => x.Code == code, ct);
            if (plan is null) continue;
            plan.PriceUzs = p.PriceUzs;
            plan.MaxUsers = p.MaxUsers < 0 ? 0 : p.MaxUsers;
            plan.MaxPoints = p.MaxPoints < 1 ? 1 : p.MaxPoints;
            plan.UpdatedAtUtc = row.UpdatedAtUtc;
        }

        await _context.SaveChangesAsync(ct);
        // Kesh — commit'dan KEYIN va MAJBURIY (yuqoridagi izoh).
        await _provider.ReloadAsync(ct);

        await _audit.LogActionAsync(
            entityType: "PlatformSettings", entityId: Guid.Empty, action: "Updated",
            userId: superAdminUserId,
            payload: new { row.GraceDays, row.RestrictAfterGrace, row.FullBlockAfterDays }, ct);

        return await GetAsync(ct);
    }

    /// <summary>Qator har doim bitta (Id = 1); migratsiya uni seed qiladi.</summary>
    private async Task<PlatformSettings> LoadAsync(CancellationToken ct)
    {
        var row = await _context.PlatformSettings.FirstOrDefaultAsync(ct);
        if (row is not null) return row;

        // Himoya qatlami: seed biror sababga ko'ra yo'qolsa, ish to'xtamasin.
        row = new PlatformSettings { Id = 1, UpdatedAtUtc = DateTime.UtcNow };
        _context.PlatformSettings.Add(row);
        await _context.SaveChangesAsync(ct);
        return row;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
