using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Bulut tomoni: do'konning birinchi to'ldirilishi uchun ma'lumot beradi.
///
/// <para>Bitta so'rov — bitta jadvalning bitta bo'lagi. Sabab va umumiy
/// tuzilish <see cref="SyncSnapshotDto"/> izohida.</para>
/// </summary>
public class SyncSnapshotService : ISyncSnapshotService
{
    private readonly IAppDbContext _context;

    /// <summary>
    /// Bir so'rovdagi eng ko'p qator soni.
    /// </summary>
    /// <remarks>
    /// <para>Bu son ikki tomondan cheklangan. Kattaroq bo'lsa do'konning
    /// javobni xotiraga sig'dirishi va sekin aloqada uni oxirigacha olishi
    /// qiyinlashadi — uzilish esa butun bo'lakni bekor qiladi. Kichikroq
    /// bo'lsa yuz minglab qatorli do'kon uchun so'rovlar soni keraksiz
    /// ko'payadi.</para>
    /// </remarks>
    public const int MaxTake = 500;

    public SyncSnapshotService(IAppDbContext context) => _context = context;

    public async Task<SyncSnapshotDto> GetAsync(
        int marketId, string table, int after, int take, CancellationToken ct = default)
    {
        take = take <= 0 ? MaxTake : Math.Min(take, MaxTake);
        after = Math.Max(0, after);

        var data = new SyncPushDto();
        int total;

        switch (table)
        {
            case SnapshotTables.ProductCategories:
                (data.ProductCategories, total) = await PageAsync(
                    _context.ProductCategories.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Suppliers:
                (data.Suppliers, total) = await PageAsync(
                    _context.Suppliers.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Customers:
                (data.Customers, total) = await PageAsync(
                    _context.Customers.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Products:
                (data.Products, total) = await PageAsync(
                    _context.Products.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Shifts:
                (data.Shifts, total) = await PageAsync(
                    _context.Shifts.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Sales:
                (data.Sales, total) = await PageAsync(
                    _context.Sales.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            // Qatorlarda MarketId YO'Q — ular otasi orqali chegaralanadi.
            // Chegara baribir qat'iy: boshqa do'konning cheki tanlanmaydi.
            case SnapshotTables.SaleItems:
                (data.SaleItems, total) = await PageAsync(
                    _context.SaleItems
                        .Where(x => _context.Sales.Any(s => s.Id == x.SaleId && s.MarketId == marketId))
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Payments:
                (data.Payments, total) = await PageAsync(
                    _context.Payments.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Debts:
                (data.Debts, total) = await PageAsync(
                    _context.Debts.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.SaleReturns:
                (data.SaleReturns, total) = await PageAsync(
                    _context.SaleReturns.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.SaleReturnItems:
                (data.SaleReturnItems, total) = await PageAsync(
                    _context.SaleReturnItems
                        .Where(x => _context.SaleReturns.Any(
                            r => r.Id == x.SaleReturnId && r.MarketId == marketId))
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.ZakupReceipts:
                (data.ZakupReceipts, total) = await PageAsync(
                    _context.ZakupReceipts.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.Zakups:
                (data.Zakups, total) = await PageAsync(
                    _context.Zakups.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.CashMovements:
                (data.CashMovements, total) = await PageAsync(
                    _context.CashMovements.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            case SnapshotTables.StockMovements:
                (data.StockMovements, total) = await PageAsync(
                    _context.StockMovements.Where(x => x.MarketId == marketId)
                        .OrderBy(x => x.Id), after, take, ct);
                break;

            default:
                // Noma'lum nom — bo'sh javob emas, XATO. Bo'sh javob do'konga
                // «bu jadval tugadi» bo'lib ko'rinar va u jimgina keyingisiga
                // o'tardi: jadval umuman tushmasdi va buni hech kim sezmasdi.
                throw new ArgumentOutOfRangeException(nameof(table), table, "Noma'lum jadval.");
        }

        var fetched = after + RowCount(data, table);
        return new SyncSnapshotDto(table, fetched >= total ? null : fetched.ToString(), total, data);
    }

    /// <summary>Bir bo'lak va jadvaldagi jami son.</summary>
    private static async Task<(List<T> Rows, int Total)> PageAsync<T>(
        IOrderedQueryable<T> query, int after, int take, CancellationToken ct)
        where T : class
    {
        // Jami son HAR so'rovda olinadi: do'kon shu bilan hammasini
        // olganini tekshiradi. Narxi arzon — bu indeks bo'yicha sanash.
        var total = await query.CountAsync(ct);
        var rows = await query.AsNoTracking().Skip(after).Take(take).ToListAsync(ct);
        return (rows, total);
    }

    private static int RowCount(SyncPushDto data, string table) => table switch
    {
        SnapshotTables.ProductCategories => data.ProductCategories.Count,
        SnapshotTables.Suppliers => data.Suppliers.Count,
        SnapshotTables.Customers => data.Customers.Count,
        SnapshotTables.Products => data.Products.Count,
        SnapshotTables.Shifts => data.Shifts.Count,
        SnapshotTables.Sales => data.Sales.Count,
        SnapshotTables.SaleItems => data.SaleItems.Count,
        SnapshotTables.Payments => data.Payments.Count,
        SnapshotTables.Debts => data.Debts.Count,
        SnapshotTables.SaleReturns => data.SaleReturns.Count,
        SnapshotTables.SaleReturnItems => data.SaleReturnItems.Count,
        SnapshotTables.ZakupReceipts => data.ZakupReceipts.Count,
        SnapshotTables.Zakups => data.Zakups.Count,
        SnapshotTables.CashMovements => data.CashMovements.Count,
        SnapshotTables.StockMovements => data.StockMovements.Count,
        _ => 0,
    };
}
