using System.Net.Http.Json;
using Buildix.Application.Common;
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

            // 4. Tovarlarning EGASI boshqaradigan maydonlari.
            var productCount = await ApplyProductsAsync(payload.ProductsOrEmpty, marketId, ct);

            state ??= NewState(marketId);
            state.PullWatermark = payload.NextSince;
            state.LastPulledAtUtc = _clock.GetUtcNow().UtcDateTime;
            state.LastError = null;

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Cloud pull applied: market={MarketChanged} users={UserCount} products={ProductCount} watermark={Watermark:O}",
                payload.Market is not null, payload.Users.Count, productCount, payload.NextSince);

            return ShopSyncResult.Ok(payload.Market is not null, payload.Users.Count);
        });
    }

    /// <summary>
    /// Egasi masofadan o'zgartirgan tovar maydonlarini qo'llaydi.
    /// </summary>
    /// <remarks>
    /// <para><b>QOLDIQ HECH QACHON o'zgarmaydi.</b> Tovar do'konda jismonan
    /// turadi va u yerda sotiladi — qoldiqni faqat do'kon biladi. Bulutdagi
    /// son oxirgi yuborishdagi nusxa va uni qaytarish o'sha payt sotilgan
    /// tovarni «tiriltirib» yuborardi: kassir omborda yo'q narsani sotishga
    /// urinardi va buni faqat mijoz oldida bilardi.</para>
    ///
    /// <para><b>Yangi tovar YARATILMAYDI.</b> Bu yerda faqat mavjudlari
    /// yangilanadi. Bulutda tovar yaratish oqimi yo'q — u do'konda tug'iladi
    /// va push bilan yuqoriga chiqadi. Noma'lum id kelsa, bu bulutdagi
    /// nosozlik yoki boshqa do'konning yozuvi bo'lishi mumkin.</para>
    ///
    /// <para><b>Nega vaqt solishtiriladi.</b> Do'kon o'z tovarini yuborgach,
    /// bulut unga O'Z vaqtini qo'yadi va keyingi tortishda o'sha yozuv
    /// qaytib keladi. Qiymatlar bir xil bo'lsa EF hech narsani o'zgargan deb
    /// belgilamaydi va halqa shu yerda uziladi. Lekin do'konda o'sha payt
    /// YANGIROQ o'zgarish bo'lgan bo'lsa, uni eski nusxa bilan bosib
    /// yuborish mumkin emas.</para>
    /// </remarks>
    private async Task<int> ApplyProductsAsync(
        IReadOnlyList<SyncProductDto> incoming, int marketId, CancellationToken ct)
    {
        if (incoming.Count == 0) return 0;

        var ids = incoming.Select(p => p.Id).ToList();
        var local = await _context.Products
            .IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id) && p.MarketId == marketId)
            .ToDictionaryAsync(p => p.Id, ct);

        var applied = 0;
        foreach (var dto in incoming)
        {
            if (!local.TryGetValue(dto.Id, out var product)) continue;

            // Do'kondagi yozuv yangiroq bo'lsa — tegmaymiz.
            var cloudTime = dto.UpdatedAt.UtcDateTime;
            if (DateTime.SpecifyKind(product.UpdatedAt, DateTimeKind.Utc) > cloudTime) continue;

            product.Name = dto.Name;
            product.CostPrice = dto.CostPrice;
            product.SalePrice = dto.SalePrice;
            product.MinSalePrice = dto.MinSalePrice;
            product.MinThreshold = dto.MinThreshold;
            product.Sku = dto.Sku;
            product.Barcode = dto.Barcode;
            product.IsHidden = dto.IsHidden;
            product.IsDeleted = dto.IsDeleted;
            // Quantity ATAYLAB yo'q.

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Bir chaqiruvdagi eng ko'p bo'lak soni.
    /// </summary>
    /// <remarks>
    /// Chegara ATAYLAB bor: aks holda katta do'konda birinchi nusxa fon
    /// xizmatini o'n daqiqalab band qilar va shu vaqt ichida na yuborish,
    /// na keyingi tortish bajarilardi. Chegaraga yetilsa qolgani keyingi
    /// aylanishda davom etadi — holat bazada saqlanadi.
    /// </remarks>
    private const int MaxPagesPerRun = 40;

    /// <inheritdoc />
    public async Task<ShopSeedResult> SeedAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return ShopSeedResult.Skipped();

        var state = await _context.SyncStates.FirstOrDefaultAsync(ct);
        // Tortish hali bo'lmagan: do'kon o'z raqamini bilmaydi va kelgan
        // savdolarni bog'laydigan xodimlar ham yo'q.
        if (state is null || state.MarketId == 0) return ShopSeedResult.Skipped();
        if (state.SeedCompletedAtUtc is not null) return ShopSeedResult.Skipped();

        var tables = SnapshotTables.InOrder;
        var index = state.SeedTable is null ? 0 : IndexOfTable(tables, state.SeedTable);
        // Nom tanilmadi (eskirgan holat) — noldan boshlaymiz. Yozish
        // «yo'q bo'lsa qo'sh» qoidasida, ya'ni takror zarar qilmaydi.
        if (index < 0) index = 0;
        var after = int.TryParse(state.SeedAfter, out var parsed) ? parsed : 0;

        var written = 0;
        var pages = 0;

        var client = _http.CreateClient("cloud");
        client.BaseAddress = new Uri(_options.Url!);
        client.DefaultRequestHeaders.Add("X-Terminal-Key", _options.TerminalKey!);

        while (index < tables.Count)
        {
            if (pages++ >= MaxPagesPerRun)
            {
                _logger.LogInformation(
                    "Nusxa davom etmoqda: {Table}, {Rows} qator yozildi", tables[index], written);
                return ShopSeedResult.Ok(completed: false, written);
            }

            var table = tables[index];
            SyncSnapshotDto? page;
            try
            {
                var response = await client.GetAsync(
                    $"api/sync/snapshot?table={table}&after={after}", ct);

                if (!response.IsSuccessStatusCode)
                {
                    var reason = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Kompyuter bulutda tanilmadi — uni qaytadan bog'lash kerak."
                        : $"Bulut javobi: {(int)response.StatusCode}";
                    return await FailSeedAsync(state, reason, ct);
                }

                page = await response.Content.ReadFromJsonAsync<SyncSnapshotDto>(
                    EntityWireFormat.Options, ct);
            }
            catch (HttpRequestException ex)
            {
                return await FailSeedAsync(state, "Bulutga ulanib bo'lmadi: " + ex.Message, ct);
            }
            catch (TaskCanceledException)
            {
                return await FailSeedAsync(state, "Bulut javob bermadi (vaqt tugadi).", ct);
            }

            if (page is null)
                return await FailSeedAsync(state, "Bulut bo'sh javob qaytardi.", ct);

            // Qatorlar va JOY BELGISI bitta tranzaksiyada. Alohida saqlansa,
            // oraliqda uzilish belgini oldinga surib, qatorlarni yozmay
            // qoldirardi — o'sha bo'lak do'konga hech qachon yetib bormasdi
            // va nusxa «tugadi» deb hisoblanardi.
            var applied = await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                var count = await ApplySnapshotAsync(page.Data, state.MarketId, ct);

                if (page.NextAfter is null)
                {
                    // Jadval tugadi — keyingisiga o'tamiz.
                    var next = index + 1;
                    state.SeedTable = next < tables.Count ? tables[next] : null;
                    state.SeedAfter = null;
                    if (next >= tables.Count)
                        state.SeedCompletedAtUtc = _clock.GetUtcNow().UtcDateTime;
                }
                else
                {
                    state.SeedTable = table;
                    state.SeedAfter = page.NextAfter;
                }

                state.LastError = null;
                await _unitOfWork.SaveChangesAsync(ct);
                return count;
            });

            written += applied;

            if (page.NextAfter is null)
            {
                index++;
                after = 0;
            }
            else
            {
                after = int.TryParse(page.NextAfter, out var n) ? n : after + 1;
            }
        }

        _logger.LogInformation("Birinchi nusxa TUGADI: {Rows} qator yozildi", written);
        return ShopSeedResult.Ok(completed: true, written);
    }

    /// <summary>
    /// Kelgan qatorlarni do'kon bazasiga yozadi — TASHQI KALIT tartibida.
    /// </summary>
    /// <remarks>
    /// <para><b>Qoida: yo'q bo'lsa qo'shiladi, bor bo'lsa TEGILMAYDI.</b>
    /// Nusxa do'kon bo'sh bo'lganda olinadi, ya'ni to'qnashuv kutilmaydi.
    /// Lekin uzilishdan keyin bo'lak qayta kelishi mumkin va o'shanda
    /// do'konda allaqachon o'zgargan yozuvni eski nusxa bilan bosib
    /// yuborish — sotilgan tovarni «tiriltirish» bilan barobar.</para>
    /// </remarks>
    private async Task<int> ApplySnapshotAsync(SyncPushDto data, int marketId, CancellationToken ct)
    {
        var n = 0;
        n += await AddMissingAsync(_context.ProductCategories, data.ProductCategories, x => x.Id, ct);
        n += await AddMissingAsync(_context.Suppliers, data.Suppliers, x => x.Id, ct);
        n += await AddMissingAsync(_context.Customers, data.Customers, x => x.Id, ct);
        n += await AddMissingAsync(_context.Products, data.Products, x => x.Id, ct);
        n += await AddMissingAsync(_context.Shifts, data.Shifts, x => x.Id, ct);
        n += await AddMissingAsync(_context.Sales, data.Sales, x => x.Id, ct);
        n += await AddMissingAsync(_context.SaleItems, data.SaleItems, x => x.Id, ct);
        n += await AddMissingAsync(_context.Payments, data.Payments, x => x.Id, ct);
        n += await AddMissingAsync(_context.Debts, data.Debts, x => x.Id, ct);
        n += await AddMissingAsync(_context.SaleReturns, data.SaleReturns, x => x.Id, ct);
        n += await AddMissingAsync(_context.SaleReturnItems, data.SaleReturnItems, x => x.Id, ct);
        n += await AddMissingAsync(_context.ZakupReceipts, data.ZakupReceipts, x => x.Id, ct);
        n += await AddMissingAsync(_context.Zakups, data.Zakups, x => x.Id, ct);
        n += await AddMissingAsync(_context.CashMovements, data.CashMovements, x => x.Id, ct);
        n += await AddMissingAsync(_context.StockMovements, data.StockMovements, x => x.Id, ct);
        return n;
    }

    /// <remarks>
    /// Kalit IFODA sifatida olinadi, oddiy funksiya sifatida emas: u
    /// so'rovga tushadi va uni SQL ga tarjima qilish kerak. Funksiya bo'lsa
    /// EF butun jadvalni xotiraga tortib, filtrni shu yerda bajarardi —
    /// yuz minglab qatorli do'konda bu birinchi nusxadayoq bilinardi.
    /// </remarks>
    private static async Task<int> AddMissingAsync<TEntity, TKey>(
        DbSet<TEntity> set,
        List<TEntity> rows,
        System.Linq.Expressions.Expression<Func<TEntity, TKey>> key,
        CancellationToken ct)
        where TEntity : class
        where TKey : notnull
    {
        if (rows.Count == 0) return 0;

        var read = key.Compile();
        var ids = rows.Select(read).ToList();

        // IgnoreQueryFilters — o'chirilgan yozuv ham MAVJUD hisoblanadi:
        // usiz u har nusxada qayta qo'shilib, kalit takrorlanishiga urilardi.
        var existing = await set
            .IgnoreQueryFilters()
            .Select(key)
            .Where(id => ids.Contains(id))
            .ToListAsync(ct);

        var have = existing.ToHashSet();
        var added = 0;
        foreach (var row in rows)
        {
            if (!have.Add(read(row))) continue;   // bazada bor yoki bo'lakda takror
            set.Add(row);
            added++;
        }

        return added;
    }

    private async Task<ShopSeedResult> FailSeedAsync(SyncState state, string error, CancellationToken ct)
    {
        state.LastError = error;
        await _unitOfWork.SaveChangesAsync(ct);
        _logger.LogWarning("Nusxa olinmadi: {Error}", error);
        return ShopSeedResult.Failed(error);
    }

    /// <summary>Jadval nomining tartibdagi o'rni; tanilmasa -1.</summary>
    private static int IndexOfTable(IReadOnlyList<string> tables, string name)
    {
        for (var i = 0; i < tables.Count; i++)
            if (tables[i] == name) return i;
        return -1;
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
        // Manzildagi nom. Ilgari u KO'CHIRILMASDI va do'kon bazasida bo'sh
        // qolardi — kirish o'tar, lekin interfeys ish ekraniga o'tolmasdi,
        // chunki o'tish aynan shu qiymat bo'yicha bajariladi. Xato hech
        // qayerga yozilmasdi: kassir bosgan tugma shunchaki javob
        // bermaydigandek ko'rinardi.
        market.Subdomain = dto.Subdomain;
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
