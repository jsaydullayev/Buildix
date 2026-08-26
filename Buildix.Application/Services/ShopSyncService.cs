using System.Net.Http.Json;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// Do'kon nusxasi tomonidagi sinxronizatsiya: bulutdan market va xodimlarni
/// olib, lokal bazaga yozadi.
///
/// <para><b>Nima uchun bu bor.</b> Yangi o'rnatilgan do'kon bazasi BO'SH —
/// na market, na foydalanuvchi. Ya'ni ilova ochiladi, lekin kirish oynasidan
/// nariga o'tib bo'lmaydi. Birinchi tortish aynan shu holatni tugatadi.</para>
///
/// <para><b>Yozuvlar ID bo'yicha ustiga yoziladi.</b> Xodimlar va market
/// bulutga tegishli, ya'ni do'kon ularni o'zgartirmaydi va birlashtirish
/// qoidasi kerak emas: kelgan qiymat — haqiqat. Shu sababli takroriy
/// tortish zarar qilmaydi va uzilgan aloqadan keyin shunchaki qaytadan
/// so'raladi.</para>
/// </summary>
public class ShopSyncService : IShopSyncService
{
    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<ShopSyncService> _logger;
    private readonly TimeProvider _clock;
    private readonly ShopCloudOptions _options;

    public ShopSyncService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        IHttpClientFactory http,
        ShopCloudOptions options,
        ILogger<ShopSyncService> logger,
        TimeProvider? clock = null)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _http = http;
        _options = options;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsConfigured => _options.IsConfigured;

    /// <summary>
    /// Bulutdan o'zgarishlarni olib, lokal bazaga yozadi.
    ///
    /// <para>Xato bo'lsa istisno tashlamaydi: internet yo'qligi do'konda
    /// NORMAL holat va u savdoni to'xtatmasligi kerak. Sabab
    /// <see cref="SyncState.LastError"/> ga yoziladi.</para>
    /// </summary>
    public async Task<ShopSyncResult> PullAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return ShopSyncResult.Skipped("Bulut sozlanmagan — do'kon hali bog'lanmagan.");

        var state = await _context.SyncStates.FirstOrDefaultAsync(ct);
        var since = state?.PullWatermark ?? DefaultWatermark;

        SyncPullDto? payload;
        try
        {
            var client = _http.CreateClient("cloud");
            client.BaseAddress = new Uri(_options.Url!);
            client.DefaultRequestHeaders.Add("X-Terminal-Key", _options.TerminalKey!);

            var response = await client.GetAsync(
                $"api/sync/pull?since={Uri.EscapeDataString(since.ToString("O"))}", ct);

            if (!response.IsSuccessStatusCode)
            {
                // 401 alohida: kalit bekor qilingan yoki noto'g'ri. Buni
                // oddiy aloqa uzilishidan ajratish SHART — birinchisi o'z-o'zidan
                // tuzalmaydi va odam aralashuvini talab qiladi.
                var reason = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Kompyuter bulutda tanilmadi — uni qaytadan bog'lash kerak."
                    : $"Bulut javobi: {(int)response.StatusCode}";
                return await FailAsync(state, reason, ct);
            }

            payload = await response.Content.ReadFromJsonAsync<SyncPullDto>(ct);
        }
        catch (HttpRequestException ex)
        {
            return await FailAsync(state, "Bulutga ulanib bo'lmadi: " + ex.Message, ct);
        }
        catch (TaskCanceledException)
        {
            return await FailAsync(state, "Bulut javob bermadi (vaqt tugadi).", ct);
        }

        if (payload is null)
            return await FailAsync(state, "Bulut bo'sh javob qaytardi.", ct);

        var marketId = payload.Market?.Id ?? state?.MarketId ?? 0;
        if (marketId == 0)
        {
            // Birinchi tortishda market kelmasa, keyingi qadamlar uchun asos
            // yo'q. Bu bulut tomondagi nosozlik — jimgina o'tkazib yuborilsa,
            // do'kon abadiy bo'sh qolardi.
            return await FailAsync(state, "Bulut do'kon ma'lumotini qaytarmadi.", ct);
        }

        // Yozuvlar va suv belgisi BITTA tranzaksiyada. Belgi alohida saqlansa,
        // yozish yarim yo'lda uzilganda belgi oldinga surilib qolar va o'sha
        // o'zgarishlar do'konga hech qachon yetib bormasdi.
        return await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // ── Tartib MUHIM ────────────────────────────────────────────────
            // Market o'z egasiga (foydalanuvchiga) ishora qiladi, foydalanuvchi
            // esa marketga. Ikkalasi ham YANGI bo'lsa, EF qaysi birini oldin
            // yozishni hal qila olmaydi va «circular dependency» bilan
            // to'xtaydi — ya'ni yangi do'konning BIRINCHI tortishi butunlay
            // ishlamaydi. Bulut tomonida do'kon yaratilganda ham aynan shu
            // uch qadam qo'llanadi (RegistrationRequestService).
            //
            // 1. Xodimlar marketsiz yoziladi.
            var touched = new List<User>();
            foreach (var user in payload.Users) touched.Add(await UpsertUserAsync(user, ct));
            await _unitOfWork.SaveChangesAsync(ct);

            // 2. Market — endi uning egasi mavjud.
            if (payload.Market is not null)
            {
                await UpsertMarketAsync(payload.Market, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            // 3. Xodimlar marketga bog'lanadi. Market DTO dan EMAS, shu
            //    tortishda aniqlangan qiymatdan: bulut buzilgan javob yuborsa
            //    ham do'konga begona xodim yozilib qolmasligi kerak.
            foreach (var user in touched) user.MarketId = marketId;

            state ??= NewState(marketId);
            state.PullWatermark = payload.NextSince;
            state.LastPulledAtUtc = _clock.GetUtcNow().UtcDateTime;
            state.LastError = null;

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Cloud pull applied: market={MarketChanged} users={UserCount} watermark={Watermark:O}",
                payload.Market is not null, payload.Users.Count, payload.NextSince);

            return ShopSyncResult.Ok(payload.Market is not null, payload.Users.Count);
        });
    }

    /// <summary>2000-yil: Npgsql eng kichik sanani '-infinity' ga aylantiradi.</summary>
    private static readonly DateTimeOffset DefaultWatermark =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private SyncState NewState(int marketId)
    {
        var created = new SyncState { MarketId = marketId, PullWatermark = DefaultWatermark };
        _context.SyncStates.Add(created);
        return created;
    }

    private async Task UpsertMarketAsync(SyncMarketDto dto, CancellationToken ct)
    {
        var market = await _context.Markets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == dto.Id, ct);

        if (market is null)
        {
            // Id ATAYLAB bulutdagidek qo'yiladi. Do'kon va bulut bir xil
            // raqamdan foydalanishi shart: aks holda keyin yuboriladigan har
            // bir savdo boshqa do'konga tegishli bo'lib qolardi.
            market = new Market { Id = dto.Id };
            _context.Markets.Add(market);
        }

        market.Name = dto.Name;
        market.City = dto.City;
        market.Plan = Enum.TryParse<PlanCode>(dto.Plan, out var plan) ? plan : market.Plan;
        market.ExpiresAt = dto.ExpiresAt?.UtcDateTime;
        market.IsActive = dto.IsActive;
        market.IsBlocked = dto.IsBlocked;
        market.BlockedReason = dto.BlockedReason;
        market.OwnerId = dto.OwnerId;
    }

    /// <summary>
    /// Xodimni yozadi va uni qaytaradi. Market ATAYLAB qo'yilmaydi — u
    /// yuqoridagi uchinchi qadamda, market yozilgandan keyin belgilanadi.
    /// </summary>
    private async Task<User> UpsertUserAsync(SyncUserDto dto, CancellationToken ct)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == dto.Id, ct);

        if (user is null)
        {
            user = new User { Id = dto.Id };
            _context.Users.Add(user);
        }

        user.Username = dto.Username;
        user.FullName = dto.FullName;
        // Parol hash'i — do'kon kirishni O'ZI tekshiradi va busiz internetsiz
        // ishlay olmaydi.
        user.PasswordHash = dto.PasswordHash;
        user.Phone = dto.Phone;
        user.Role = (Role)dto.Role;
        user.IsActive = dto.IsActive;
        user.IsDeleted = dto.IsDeleted;
        user.Permissions = dto.Permissions.ToList();
        user.IsPermissionsCustomized = dto.IsPermissionsCustomized;
        if (Enum.TryParse<Language>(dto.Language, out var language)) user.Language = language;
        user.MaxDebtPerCheck = dto.MaxDebtPerCheck;
        user.MaxDiscountPercent = dto.MaxDiscountPercent;

        return user;
    }

    private async Task<ShopSyncResult> FailAsync(SyncState? state, string reason, CancellationToken ct)
    {
        _logger.LogWarning("Cloud pull failed: {Reason}", reason);

        if (state is not null)
        {
            state.LastError = reason.Length > 500 ? reason[..500] : reason;
            try { await _unitOfWork.SaveChangesAsync(ct); }
            catch (Exception) { /* holatni yozib bo'lmasa ham savdo davom etadi */ }
        }

        return ShopSyncResult.Failed(reason);
    }
}
