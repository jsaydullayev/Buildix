using System.Net.Http.Json;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Common;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Buildix.Application.Services;

/// <summary>
/// DO'KON tomoni: o'zgargan yozuvlarni bulutga yuboradi.
///
/// <para><b>Har jadval o'z belgisi bilan.</b> Bitta so'rovdagi yozuvlar soni
/// cheklangan (kun bo'yi to'plangan ma'lumot bitta ulkan so'rovga aylanmasin).
/// Yagona belgi bo'lsa, chegaraga urilgan jadval qolganlarini to'sib qo'yardi
/// — sabab <see cref="SyncPushState"/> da.</para>
///
/// <para><b>Belgi faqat bulut QABUL QILGANDAN keyin suriladi.</b> Aks holda
/// aloqa uzilgan paytdagi yozuvlar bulutga hech qachon yetib bormas va
/// egasining telefonidagi raqamlar jimgina kam ko'rsatardi — savdo esa
/// do'konda bemalol davom etaverardi.</para>
/// </summary>
public class ShopPushService : IShopPushService
{
    /// <summary>
    /// Bitta so'rovda bir jadvaldan nechta qator. O'zbekistondagi do'kon
    /// interneti sekin bo'lishi mumkin, shuning uchun so'rov kichik bo'lgani
    /// ma'qul: uzilsa oz narsa qaytadan yuboriladi.
    /// </summary>
    private const int BatchSize = 200;

    /// <summary>Bitta chaqiruvdagi eng ko'p yuborish soni.</summary>
    private const int MaxPassesPerRun = 25;

    private static readonly DateTimeOffset DefaultWatermark =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpClientFactory _http;
    private readonly ShopCloudOptions _options;
    private readonly ILogger<ShopPushService> _logger;
    private readonly TimeProvider _clock;

    public ShopPushService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        IHttpClientFactory http,
        ShopCloudOptions options,
        ILogger<ShopPushService> logger,
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
    /// Navbat bo'shaguncha ketma-ket yuboradi.
    ///
    /// <para><b>Nega halqa.</b> Bitta so'rovda bir jadvaldan 200 qator
    /// ketadi. Do'kon bir kun aloqasiz ishlagan bo'lsa, orqada minglab yozuv
    /// to'planadi va ular besh daqiqada 200 tadan bo'shab, soatlab davom
    /// etardi — egasining telefonidagi raqamlar shuncha vaqt orqada
    /// qolardi.</para>
    ///
    /// <para>Qadamlar soni cheklangan: bitta chaqiruv cheksiz cho'zilib
    /// ketmasin, qolgani keyingi safar davom etadi.</para>
    /// </summary>
    public async Task<ShopPushResult> PushAsync(CancellationToken ct = default)
    {
        var total = 0;
        for (var pass = 0; pass < MaxPassesPerRun; pass++)
        {
            var result = await PushOnceAsync(ct);

            // O'tkazib yuborildi (hali bog'lanmagan yoki bulutdan hech narsa
            // tortilmagan). Bu na muvaffaqiyat, na xato — holatga TEGMAYMIZ.
            // Aks holda bog'lanmagan do'kon «hozirgina yubordi» bo'lib
            // ko'rinardi: `Skipped` ham `Success = true` qaytaradi.
            if (result.Success && result.Error is not null) return result;

            // Uzilish yuz bersa, shu paytgacha yetkazilgani saqlanib qoladi:
            // belgilar har qadamda alohida suriladi.
            if (!result.Success)
            {
                // Yarim yo'lda uzilgan bo'lsa ham bu MUVAFFAQIYAT emas: qolgan
                // qatorlar hali bulutda yo'q va sabab yozilishi kerak.
                await RecordOutcomeAsync(result.Error, ct);
                return total > 0 ? ShopPushResult.Ok(total) : result;
            }

            total += result.Rows;
            if (result.Rows == 0) break;   // navbat bo'shadi
        }

        await RecordOutcomeAsync(null, ct);
        return ShopPushResult.Ok(total);
    }

    /// <summary>
    /// Yuborishning natijasini do'kon bazasiga yozadi.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari push xatosi FAQAT jurnalga tushardi. Oqibati og'ir edi:
    /// tashqi kalit xatosi yuz bersa, do'kon har daqiqada aynan o'sha paketni
    /// qayta yuborar va har safar yiqilardi, ekranda esa hech qanday iz
    /// qolmasdi. Egasining telefonida «hozirgina sinxron» degan yashil belgi
    /// turardi, chunki u aloqa vaqtiga qarardi — ma'lumot esa haftalab
    /// kelmasdi.</para>
    ///
    /// <para>Holatni yozish O'ZI xato bersa, savdo to'xtamasligi kerak: bu
    /// yordamchi ma'lumot, pul harakati emas.</para>
    /// </remarks>
    private async Task RecordOutcomeAsync(string? error, CancellationToken ct)
    {
        try
        {
            var state = await _context.SyncStates.FirstOrDefaultAsync(ct);
            if (state is null) return;

            if (error is null)
            {
                state.LastPushedAtUtc = _clock.GetUtcNow().UtcDateTime;
                state.LastPushError = null;
            }
            else
            {
                state.LastPushError = error.Length > 500 ? error[..500] : error;
            }

            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push holatini yozib bo'lmadi");
        }
    }

    private async Task<ShopPushResult> PushOnceAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return ShopPushResult.Skipped("Bulut sozlanmagan — do'kon hali bog'lanmagan.");

        var marketId = (await _context.SyncStates.FirstOrDefaultAsync(ct))?.MarketId ?? 0;
        if (marketId == 0)
        {
            // Bulutdan hali hech narsa tortilmagan, ya'ni do'kon o'z raqamini
            // bilmaydi. Yuborishdan oldin tortish SHART.
            return ShopPushResult.Skipped("Do'kon hali bulutdan ma'lumot olmagan.");
        }

        var states = await _context.SyncPushStates
            .Where(s => s.MarketId == marketId)
            .ToDictionaryAsync(s => s.TableName, ct);

        var payload = new SyncPushDto();
        var sent = new Dictionary<string, (DateTimeOffset Watermark, Guid LastId)>();

        payload.Products = await CollectAsync(
            _context.Products.Where(x => x.MarketId == marketId), states, sent, ct);
        payload.Customers = await CollectAsync(
            _context.Customers.Where(x => x.MarketId == marketId), states, sent, ct);
        payload.Shifts = await CollectAsync(
            _context.Shifts.Where(x => x.MarketId == marketId), states, sent, ct);
        payload.Sales = await CollectAsync(
            _context.Sales.Where(x => x.MarketId == marketId), states, sent, ct);
        // SaleItem da MarketId YO'Q — u marketga faqat o'z sotuvi orqali
        // tegishli, shuning uchun filtr sotuv orqali qo'yiladi.
        payload.SaleItems = await CollectAsync(
            _context.SaleItems.Where(x => _context.Sales
                .Any(sale => sale.Id == x.SaleId && sale.MarketId == marketId)), states, sent, ct);
        payload.Payments = await CollectAsync(
            _context.Payments.Where(x => x.MarketId == marketId), states, sent, ct);

        if (payload.IsEmpty) return ShopPushResult.Ok(0);

        try
        {
            var client = _http.CreateClient("cloud");
            client.BaseAddress = new Uri(_options.Url!);
            client.DefaultRequestHeaders.Add("X-Terminal-Key", _options.TerminalKey!);

            var response = await client.PostAsJsonAsync(
                "api/sync/push", payload, EntityWireFormat.Options, ct);

            if (!response.IsSuccessStatusCode)
            {
                var reason = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Kompyuter bulutda tanilmadi — uni qaytadan bog'lash kerak."
                    : $"Bulut javobi: {(int)response.StatusCode}";
                _logger.LogWarning("Push rejected: {Reason}", reason);
                return ShopPushResult.Failed(reason);
            }

            // Otasi hali yetib bormagan qatorlar bo'lsa, O'SHA jadval belgisi
            // surilmaydi — keyingi urinishda otasi o'tgach, ular ham o'tadi.
            // Belgi surilsa, qator abadiy yo'qolardi.
            var accepted = await response.Content.ReadFromJsonAsync<SyncPushResultDto>(
                EntityWireFormat.Options, ct);
            if (accepted?.Deferred is { Count: > 0 } waiting)
            {
                foreach (var table in waiting.Keys) sent.Remove(table);
            }
        }
        catch (HttpRequestException ex)
        {
            return ShopPushResult.Failed("Bulutga ulanib bo'lmadi: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            return ShopPushResult.Failed("Bulut javob bermadi (vaqt tugadi).");
        }

        // Bulut qabul qildi — endi belgilarni surish mumkin.
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var (table, cursor) in sent)
        {
            if (!states.TryGetValue(table, out var state))
            {
                state = new SyncPushState { MarketId = marketId, TableName = table };
                _context.SyncPushStates.Add(state);
            }
            state.Watermark = cursor.Watermark;
            state.LastId = cursor.LastId;
            state.LastPushedAtUtc = now;
        }
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Pushed {Rows} rows to cloud", payload.TotalRows);
        return ShopPushResult.Ok(payload.TotalRows);
    }

    /// <summary>
    /// Bir jadvaldan yuborilishi kerak bo'lgan yozuvlarni oladi va yangi
    /// belgini <paramref name="sent"/> ga yozib qo'yadi.
    ///
    /// <para>Taqqoslash <c>&gt;=</c>: bitta saqlashdagi yozuvlar aynan bir xil
    /// vaqt oladi va qat'iy <c>&gt;</c> ularning bir qismini butunlay
    /// o'tkazib yuborardi. Takror yuborish zarar qilmaydi — bulut ID bo'yicha
    /// ustiga yozadi.</para>
    /// </summary>
    private async Task<List<T>> CollectAsync<T>(
        IQueryable<T> scoped,
        Dictionary<string, SyncPushState> states,
        Dictionary<string, (DateTimeOffset Watermark, Guid LastId)> sent,
        CancellationToken ct)
        where T : BaseEntity
    {
        var name = typeof(T).Name;
        var state = states.GetValueOrDefault(name);
        var sinceUtc = (state?.Watermark ?? DefaultWatermark).UtcDateTime;
        var lastId = state?.LastId ?? Guid.Empty;

        // Kursor — (vaqt, kalit) juftligi. Faqat vaqt bo'yicha `>=` bilan bir
        // xil vaqtga ega paket ABADIY qayta yuborilardi (bitta saqlashdagi
        // 200 qator aynan bir xil vaqt oladi), qat'iy `>` bilan esa uning bir
        // qismi butunlay o'tkazib yuborilardi. Juftlik ikkalasini ham hal
        // qiladi: tartib to'liq aniq va har paket oldinga siljiydi.
        //
        // IgnoreQueryFilters — o'chirilgan yozuv ham yuborilishi SHART: aks
        // holda bulutda u tirik bo'lib qolaverardi va egasining telefonida
        // bekor qilingan savdo hamon ko'rinardi.
        var rows = await scoped
            .IgnoreQueryFilters()
            .Where(x => x.UpdatedAt > sinceUtc
                     || (x.UpdatedAt == sinceUtc && x.Id.CompareTo(lastId) > 0))
            .OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Take(BatchSize)
            .AsNoTracking()
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            var last = rows[^1];
            sent[name] = (
                new DateTimeOffset(DateTime.SpecifyKind(last.UpdatedAt, DateTimeKind.Utc), TimeSpan.Zero),
                last.Id);
        }

        return rows;
    }
}
