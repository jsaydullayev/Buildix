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
/// BULUT tomoni: do'kondan kelgan yozuvlarni qabul qiladi.
///
/// <para><b>Do'konga ishonilmaydi.</b> Kelgan har bir qatorning
/// <c>MarketId</c> si kalitdan aniqlangan do'konga MAJBURAN almashtiriladi.
/// Busiz noto'g'ri sozlangan yoki buzilgan do'kon nusxasi qo'shni do'konning
/// savdolarini o'zining deb yozib yuborishi mumkin edi — va buni hech kim
/// sezmasdi, chunki hech qanday xato chiqmaydi.</para>
///
/// <para><b>ID bo'yicha ustiga yoziladi.</b> Kalitlar GUID va ular do'konda
/// yaratiladi, ya'ni bir xil yozuv ikki marta kelsa ikkinchisi birinchisining
/// ustiga tushadi. Shu sababli takroriy yuborish zarar qilmaydi va uzilgan
/// aloqadan keyin do'kon shunchaki qaytadan yuboraveradi.</para>
/// </summary>
public class SyncPushService : ISyncPushService
{
    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncPushService> _logger;

    public SyncPushService(
        IAppDbContext context, IUnitOfWork unitOfWork, ILogger<SyncPushService> logger)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<SyncPushResultDto> AcceptAsync(
        int marketId, SyncPushDto payload, CancellationToken ct = default)
    {
        var perTable = new Dictionary<string, int>();
        var deferred = new Dictionary<string, int>();

        // Butun to'plam BITTA tranzaksiyada. Yarim qabul qilingan to'plam eng
        // yomon holat bo'lardi: sotuv bor, qatorlari yo'q — va bulutdagi
        // hisobot jimgina noto'g'ri ko'rsatardi.
        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            // Tartib SyncPushDto da izohlangan: havola qilinadigan yozuvlar
            // avval.
            perTable["Product"] = await UpsertAsync(_context.Products, payload.Products, marketId, ct);
            perTable["Customer"] = await UpsertAsync(_context.Customers, payload.Customers, marketId, ct);
            perTable["Shift"] = await UpsertAsync(_context.Shifts, payload.Shifts, marketId, ct);
            perTable["Sale"] = await UpsertAsync(_context.Sales, payload.Sales, marketId, ct);

            // ── Bola yozuvlar: otasi SHU do'konniki bo'lishi shart ──────────
            // SaleItem da MarketId YO'Q — u marketga faqat o'z sotuvi orqali
            // tegishli, ya'ni uni majburan almashtirib bo'lmaydi. Tekshiruvsiz
            // do'kon QO'SHNI do'konning sotuviga qator yoki to'lov qo'shib
            // yuborishi mumkin edi: tashqi kalit buni qabul qilardi va
            // qo'shnining hisoboti jimgina buzilardi.
            var parents = await ClassifyParentsAsync(payload, marketId, ct);

            var items = Split(payload.SaleItems, x => x.SaleId, parents, "SaleItem", marketId, deferred);
            var payments = Split(payload.Payments, x => x.SaleId, parents, "Payment", marketId, deferred);

            perTable["SaleItem"] = await UpsertAsync(_context.SaleItems, items, marketId, ct);
            perTable["Payment"] = await UpsertAsync(_context.Payments, payments, marketId, ct);

            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        });

        var accepted = perTable.Values.Sum();
        if (accepted > 0)
        {
            _logger.LogInformation(
                "Push accepted from market {MarketId}: {Rows} rows ({Detail})",
                marketId, accepted, string.Join(", ", perTable.Where(p => p.Value > 0).Select(p => $"{p.Key}={p.Value}")));
        }

        return new SyncPushResultDto(accepted, perTable, deferred);
    }

    /// <summary>
    /// Kelgan yozuvlarni mavjudlari bilan solishtirib yozadi.
    ///
    /// <para><c>UpdatedAt</c> ATAYLAB ko'chirilmaydi: uni bulutning o'z
    /// <c>SaveChanges</c> i qo'yadi. Do'konning vaqtini olib qo'yish bulutdagi
    /// suv belgisini buzardi — do'kon soati orqada bo'lsa, yozuv «o'tmishda
    /// o'zgargan» bo'lib ko'rinar va uni tortadigan boshqa mijozlar
    /// o'tkazib yuborardi.</para>
    /// </summary>
    private async Task<int> UpsertAsync<T>(
        DbSet<T> table, List<T> incoming, int marketId, CancellationToken ct)
        where T : BaseEntity
    {
        if (incoming.Count == 0) return 0;

        var ids = incoming.Select(x => x.Id).ToList();
        var existing = await table
            .IgnoreQueryFilters()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var row in incoming)
        {
            ForceMarket(row, marketId);

            if (existing.TryGetValue(row.Id, out var current))
            {
                EntityWireFormat.CopyColumns(row, current, nameof(BaseEntity.Id), nameof(BaseEntity.UpdatedAt));
            }
            else
            {
                table.Add(row);
            }
        }

        return incoming.Count;
    }

    /// <summary>Ota-sotuvning holati.</summary>
    private enum Parent { Mine, Foreign, Unknown }

    /// <summary>
    /// Bola qatorlar havola qiladigan sotuvlarni uch guruhga ajratadi.
    ///
    /// <para>Farq HAL QILUVCHI: begona sotuv — bu nosozlik yoki urinish va u
    /// rad etiladi; noma'lum sotuv esa shunchaki HALI yetib bormagan va uni
    /// rad etish ma'lumotni abadiy yo'qotardi.</para>
    /// </summary>
    private async Task<Dictionary<Guid, Parent>> ClassifyParentsAsync(
        SyncPushDto payload, int marketId, CancellationToken ct)
    {
        var result = new Dictionary<Guid, Parent>();

        // Hozir kelgan sotuvlar yuqorida MarketId majburan almashtirilib
        // yozildi, ya'ni ular ta'rifi bo'yicha shu do'konniki.
        foreach (var sale in payload.Sales) result[sale.Id] = Parent.Mine;

        var referenced = payload.SaleItems.Select(x => x.SaleId)
            .Concat(payload.Payments.Select(x => x.SaleId))
            .Where(id => !result.ContainsKey(id))
            .Distinct()
            .ToList();

        if (referenced.Count == 0) return result;

        var known = await _context.Sales
            .IgnoreQueryFilters()
            .Where(s => referenced.Contains(s.Id))
            .Select(s => new { s.Id, s.MarketId })
            .ToListAsync(ct);

        foreach (var sale in known)
            result[sale.Id] = sale.MarketId == marketId ? Parent.Mine : Parent.Foreign;

        foreach (var id in referenced)
            if (!result.ContainsKey(id)) result[id] = Parent.Unknown;

        return result;
    }

    /// <summary>
    /// Qabul qilinadiganlarni ajratadi; begonalarini rad etadi, otasi hali
    /// yetib bormaganlarini esa kechiktiradi.
    /// </summary>
    private List<T> Split<T>(
        List<T> rows,
        Func<T, Guid> parentOf,
        Dictionary<Guid, Parent> parents,
        string table,
        int marketId,
        Dictionary<string, int> deferred)
    {
        var kept = new List<T>(rows.Count);
        var foreign = 0;
        var waiting = 0;

        foreach (var row in rows)
        {
            switch (parents.TryGetValue(parentOf(row), out var state) ? state : Parent.Unknown)
            {
                case Parent.Mine: kept.Add(row); break;
                case Parent.Foreign: foreign++; break;
                default: waiting++; break;
            }
        }

        if (foreign > 0)
        {
            // JIMGINA tashlab yuborilmaydi: bu yoki do'kon nusxasidagi jiddiy
            // nosozlik, yoki ataylab qilingan urinish.
            _logger.LogWarning(
                "Push from market {MarketId}: {Count} {Table} row(s) rejected — parent sale belongs to another market",
                marketId, foreign, table);
        }

        if (waiting > 0)
        {
            deferred[table] = waiting;
            _logger.LogInformation(
                "Push from market {MarketId}: {Count} {Table} row(s) deferred — parent sale not received yet",
                marketId, waiting, table);
        }

        return kept;
    }

    /// <summary>
    /// Yozuvni kalitdan aniqlangan do'konga bog'laydi.
    ///
    /// <para>Bu YAGONA chegara: qolgan hamma maydonda haqiqat manbai do'kon,
    /// lekin qaysi do'kon ekanini do'konning o'zi aytmaydi.</para>
    /// </summary>
    private static void ForceMarket<T>(T row, int marketId)
    {
        var property = typeof(T).GetProperty("MarketId");
        if (property is null || !property.CanWrite) return;

        if (property.PropertyType == typeof(int)) property.SetValue(row, marketId);
        else if (property.PropertyType == typeof(int?)) property.SetValue(row, (int?)marketId);
    }
}
