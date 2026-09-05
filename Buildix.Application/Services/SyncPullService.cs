using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Bulutdan do'konga tushadigan ma'lumot: do'konning o'zi va xodimlar.
///
/// <para><b>Nega outbox emas, suv belgisi.</b> Har bir jadvalda
/// <c>UpdatedAt</c> bor va u <c>SaveChanges</c> ichida markazlashtirilgan,
/// ya'ni «oxirgi aloqadan keyin nima o'zgardi» degan savolga javob beradigan
/// tayyor manba mavjud. Navbat jadvali bo'lsa, u ma'lumot bilan bir joyda
/// turmasligi mumkin edi: yozuv o'zgarib, navbatga tushmay qolishi — yoki
/// aksincha — mumkin. Bu yerda esa manba bitta va ular orasida farq paydo
/// bo'lishining imkoni yo'q.</para>
///
/// <para><b>Nega <c>&gt;=</c>, <c>&gt;</c> emas.</b> Bir necha yozuv bitta
/// <c>SaveChanges</c> da o'zgarsa, ular AYNAN bir xil vaqt oladi. Qat'iy
/// <c>&gt;</c> bilan keyingi so'rov o'sha vaqtdagi yozuvlarni butunlay
/// o'tkazib yuborardi. <c>&gt;=</c> esa oxirgi to'plamni qayta yuboradi —
/// bu zarar qilmaydi, chunki do'kon tomonda yozuv ID bo'yicha ustiga
/// yoziladi.</para>
///
/// <para><b>Nimaga tayanadi.</b> Suv belgisi yozuv natijadan IZ QOLDIRMASDAN
/// chiqib ketmasligiga tayanadi. Bugun bu shart bajariladi: foydalanuvchi
/// hech qachon qattiq o'chirilmaydi (faqat <c>IsDeleted</c>) va boshqa
/// do'konga ko'chirilmaydi — ikkalasi ham kodda tekshirilib ko'rildi. Agar
/// keyinchalik shunday amal qo'shilsa, do'kondagi nusxa o'sha yozuvni
/// ABADIY saqlab qolardi: bo'shatilgan kassir kirishda davom etardi va bu
/// hech qanday belgi bermasdi.</para>
/// </summary>
public class SyncPullService : ISyncPullService
{
    /// <summary>
    /// Bir javobda bir jadvaldan nechta qator.
    ///
    /// <para>Do'kon interneti sekin bo'lishi mumkin, shuning uchun javob
    /// kichik bo'lgani ma'qul. Cheklar ALOHIDA: chek qatorlari va to'lovlari
    /// bilan birga kelgani uchun bitta chek bir necha qatorga teng.</para>
    /// </summary>
    private const int ProductBatch = 500;
    private const int CustomerBatch = 500;
    private const int SaleBatch = 200;
    private const int StockBatch = 500;
    private const int CashBatch = 500;

    private readonly IAppDbContext _context;
    private readonly TimeProvider _clock;

    public SyncPullService(IAppDbContext context, TimeProvider? clock = null)
    {
        _context = context;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<SyncPullDto> PullAsync(
        int marketId, DateTimeOffset since, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();

        // Ustunlar UTC saqlaydi, shuning uchun taqqoslash ham UTC da bo'lishi
        // shart. Mijoz belgini qaysi mintaqada yuborgani ahamiyatsiz —
        // siljish aynan shu yerda yechiladi.
        var fromUtc = since.UtcDateTime;

        // IgnoreQueryFilters — bu yerda ATAYLAB. Global filtr o'chirilgan
        // xodimni yashiradi, do'kon esa aynan o'chirilganini bilishi kerak:
        // aks holda bo'shatilgan kassir do'konda abadiy ishlayveradi.
        // MarketId sharti qo'lda qo'yiladi va u yagona chegara.
        var users = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.MarketId == marketId && u.UpdatedAt >= fromUtc)
            .OrderBy(u => u.UpdatedAt)
            .ToListAsync(ct);

        // ── Do'kon HAR safar yuboriladi ──────────────────────────────────
        // Qolgan jadvallardan farqli ravishda bu yerda suv belgisi
        // QO'LLANMAYDI. Sabab: bu bitta kichkina yozuv va uni tejashdan
        // hech qanday yutuq yo'q — trafik bir necha yuz bayt. Evaziga esa
        // butun bir xatolar sinfi yo'qoladi.
        //
        // Suv belgisi bilan do'kon yozuvi FAQAT o'zgarganda kelardi, ya'ni
        // uni bir marta o'tkazib yuborgan (yoki noto'g'ri qo'llagan) nusxa
        // keyin HECH QACHON to'g'ri qiymatni ko'rmasdi: belgi allaqachon
        // oldinga surilgan va bulut o'sha yozuvni boshqa yubormasdi. Aynan
        // shu sabab bilan `Subdomain` bo'sh qolgan nusxa o'zini o'zi
        // tuzata olmasdi — uni faqat bazani o'chirib qayta bog'lash
        // qutqarardi.
        var market = await _context.Markets
            .IgnoreQueryFilters()
            .Where(m => m.Id == marketId)
            .FirstOrDefaultAsync(ct);

        var marketDto = market is null ? null : new SyncMarketDto(
            market.Id, market.Name, market.Subdomain, market.City, market.Plan.ToString(),
            AsUtc(market.ExpiresAt), market.IsActive, market.IsBlocked,
            market.BlockedReason, market.OwnerId, AsUtc(market.UpdatedAt));

        var userDtos = users.Select(u => new SyncUserDto(
            u.Id, u.Username, u.FullName, u.PasswordHash, u.Phone, (int)u.Role,
            u.IsActive, u.IsDeleted, u.Permissions, u.IsPermissionsCustomized,
            u.Language.ToString(), u.MaxDebtPerCheck, u.MaxDiscountPercent,
            AsUtc(u.UpdatedAt))).ToList();

        // ── Tovarlar: FAQAT egasi boshqaradigan maydonlar ────────────────
        // Qoldiq ATAYLAB olinmaydi — sabab SyncProductDto izohida.
        var products = await _context.Products
            .IgnoreQueryFilters()
            .Where(p => p.MarketId == marketId && p.UpdatedAt >= fromUtc)
            .OrderBy(p => p.UpdatedAt)
            .Take(ProductBatch)
            .ToListAsync(ct);

        var productDtos = products.Select(p => new SyncProductDto(
            p.Id, p.Name, p.CostPrice, p.SalePrice, p.MinSalePrice, p.MinThreshold,
            p.Sku, p.Barcode, p.IsHidden, p.IsDeleted, AsUtc(p.UpdatedAt), (int)p.Unit)).ToList();

        // ── Mijozlar: egasi paneldan boshqaradigan maydonlar ─────────────
        // Qarz ATAYLAB yo'q — u ustun emas, `Debts` qatorlaridan
        // hisoblanadi (sabab SyncCustomerDto izohida).
        var customers = await _context.Customers
            .IgnoreQueryFilters()
            .Where(c => c.MarketId == marketId && c.UpdatedAt >= fromUtc)
            .OrderBy(c => c.UpdatedAt)
            .Take(CustomerBatch)
            .ToListAsync(ct);

        var customerDtos = customers.Select(c => new SyncCustomerDto(
            c.Id, c.Phone, c.FullName, c.Comment, (int)c.CustomerType,
            c.IsRegular, c.DebtLimit, c.IsDeleted, AsUtc(c.UpdatedAt))).ToList();

        // ── Sozlamalar: faqat DO'KON ishlatadigan maydonlar ──────────────
        // Ilgari ular umuman sinxronlanmasdi va bulutda-do'konda ikkita
        // mustaqil nusxa yotardi (sabab SyncSettingsDto izohida).
        var settingsRow = await _context.MarketSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.MarketId == marketId && s.UpdatedAt >= fromUtc, ct);

        var settingsDto = settingsRow is null ? null : new SyncSettingsDto(
            settingsRow.Phone, settingsRow.Address, settingsRow.WorkingHours,
            settingsRow.ReceiptHeader, settingsRow.ReceiptFooter,
            settingsRow.AutoPrintReceipt, settingsRow.ReceiptWidthMm,
            settingsRow.SalesOnlyWhenShiftOpen, settingsRow.CashWithdrawalNeedsApproval,
            settingsRow.DebtOnlyForRegulars, settingsRow.DebtRequiresCloud,
            settingsRow.DefaultDebtLimit, settingsRow.BlockSaleBelowCost,
            settingsRow.AllowedCashDiscrepancy, settingsRow.MinStockAlertEnabled,
            settingsRow.DefaultMarkupPct, settingsRow.InactivityLogoutMinutes,
            settingsRow.AuditEnabled, AsUtc(settingsRow.UpdatedAt));

        // ── Cheklar va ularning bolalari ─────────────────────────────────
        // Bolalar ALOHIDA kursor bilan olinmaydi — ular OTASI bilan birga
        // yuboriladi. Aks holda qator otasidan oldin kelib, do'kon tomonida
        // tashqi kalit buzilardi va uni «kutish» navbatiga qo'yish kerak
        // bo'lardi. Chek o'zgarganda jami ham o'zgaradi, ya'ni tahrirlangan
        // qator otasi bilan baribir qaytadan tushadi.
        var sales = await _context.Sales
            .IgnoreQueryFilters()
            .Where(s => s.MarketId == marketId && s.UpdatedAt >= fromUtc)
            .OrderBy(s => s.UpdatedAt)
            .Take(SaleBatch)
            .ToListAsync(ct);

        var saleIds = sales.Select(s => s.Id).ToList();

        var saleDtos = sales.Select(s => new SyncSaleDto(
            s.Id, s.SaleNumber, s.RegisterCode, s.SellerId, s.ShiftId, s.CustomerId,
            (int)s.Status, s.TotalAmount, s.PaidAmount, s.DiscountAmount,
            s.IsOpeningBalance, s.IsDeleted, AsUtc(s.CreatedAt), AsUtc(s.UpdatedAt))).ToList();

        var itemDtos = saleIds.Count == 0 ? [] : await _context.SaleItems
            .IgnoreQueryFilters()
            .Where(i => saleIds.Contains(i.SaleId))
            .Select(i => new SyncSaleItemDto(
                i.Id, i.SaleId, i.ProductId, i.IsExternal, i.ExternalProductName,
                i.ExternalCostPrice, i.Quantity, i.CostPrice, i.SalePrice, i.Comment,
                AsUtc(i.UpdatedAt)))
            .ToListAsync(ct);

        var paymentDtos = saleIds.Count == 0 ? [] : await _context.Payments
            .IgnoreQueryFilters()
            .Where(p => saleIds.Contains(p.SaleId))
            .Select(p => new SyncPaymentDto(
                p.Id, p.SaleId, (int)p.PaymentType, p.Amount, p.CollectedByUserId,
                AsUtc(p.CreatedAt), AsUtc(p.UpdatedAt)))
            .ToListAsync(ct);

        var debtDtos = saleIds.Count == 0 ? [] : await _context.Debts
            .IgnoreQueryFilters()
            .Where(d => saleIds.Contains(d.SaleId))
            .Select(d => new SyncDebtDto(
                d.Id, d.SaleId, d.CustomerId, d.TotalDebt, d.RemainingDebt,
                (int)d.Status, d.DueDate == null ? null : AsUtc(d.DueDate.Value),
                AsUtc(d.UpdatedAt)))
            .ToListAsync(ct);

        // ── Ombor harakatlari: O'Z kursori bilan ─────────────────────────
        // Chekdan farqli o'laroq bular otasi bilan yurmaydi — harakat
        // tovarga bog'langan va chekdan mustaqil ravishda paydo bo'ladi
        // (xaridnoma, inventarizatsiya, tuzatish).
        var movements = await _context.StockMovements
            .IgnoreQueryFilters()
            .Where(m => m.MarketId == marketId && m.UpdatedAt >= fromUtc)
            .OrderBy(m => m.UpdatedAt)
            .Take(StockBatch)
            .Select(m => new SyncStockMovementDto(
                m.Id, m.ProductId, (int)m.Type, m.Delta, m.ResultingQty,
                m.RefNumber, m.UserId, m.Comment, AsUtc(m.CreatedAt), AsUtc(m.UpdatedAt)))
            .ToListAsync(ct);

        // Qaytarishlar — cheklar bilan birga (otasiz qaytarish ma'nosiz).
        var returnDtos = saleIds.Count == 0 ? [] : await _context.SaleReturns
            .IgnoreQueryFilters()
            .Where(r => saleIds.Contains(r.SaleId))
            .Select(r => new SyncSaleReturnDto(
                r.Id, r.SaleId, r.Number, (int)r.Reason, (int)r.RefundMethod,
                r.TotalAmount, r.Comment, AsUtc(r.CreatedAt), AsUtc(r.UpdatedAt)))
            .ToListAsync(ct);

        var returnIds = returnDtos.Select(r => r.Id).ToList();
        var returnItemDtos = returnIds.Count == 0 ? [] : await _context.SaleReturnItems
            .IgnoreQueryFilters()
            .Where(i => returnIds.Contains(i.SaleReturnId))
            .Select(i => new SyncSaleReturnItemDto(
                i.Id, i.SaleReturnId, i.SaleItemId, i.ProductId, i.ProductName,
                i.Quantity, i.UnitPrice, AsUtc(i.UpdatedAt)))
            .ToListAsync(ct);

        // ── Kassa jurnali: O'Z kursori bilan ─────────────────────────────
        // Chekdan mustaqil paydo bo'ladi (inkassatsiya, xarajat, kirim).
        var cashDtos = await _context.CashMovements
            .IgnoreQueryFilters()
            .Where(m => m.MarketId == marketId && m.UpdatedAt >= fromUtc)
            .OrderBy(m => m.UpdatedAt)
            .Take(CashBatch)
            .Select(m => new SyncCashMovementDto(
                m.Id, (int)m.Type, m.Amount, m.Category, m.RefNumber, m.Comment,
                AsUtc(m.CreatedAt), AsUtc(m.UpdatedAt)))
            .ToListAsync(ct);

        // Keyingi suv belgisi — QAYTARILGAN yozuvlarning eng kattasi, bulut
        // soati emas. Bulut vaqti olinsa, so'rov bajarilayotgan payt yozilgan
        // yozuv o'tkazib yuborilardi: uning vaqti belgidan kichik bo'lib
        // qolar, lekin u javobga tushmagan bo'lardi.
        var stamps = userDtos.Select(u => u.UpdatedAt)
            .Concat(productDtos.Select(p => p.UpdatedAt))
            .Concat(customerDtos.Select(c => c.UpdatedAt))
            .Concat(saleDtos.Select(s => s.UpdatedAt))
            .Concat(movements.Select(m => m.UpdatedAt))
            .Concat(cashDtos.Select(m => m.UpdatedAt))
            .ToList();
        if (marketDto is not null) stamps.Add(marketDto.UpdatedAt);
        if (settingsDto is not null) stamps.Add(settingsDto.UpdatedAt);

        // ── CHEGARAGA urilgan jadval belgini USHLAB turadi ───────────────
        // Har jadval o'z chegarasi bilan olinadi. Belgi barcha jadvallarning
        // eng kattasiga surilsa, chegaraga urilgan jadvalning QOLGAN
        // qatorlari abadiy o'tkazib yuborilardi: ularning vaqti yangi
        // belgidan kichik bo'lib qolar, lekin ular javobga tushmagan
        // bo'lardi. Xato chiqmasdi — yozuvlar shunchaki hech qachon
        // yetib bormasdi.
        //
        // Shuning uchun chegaraga urilgan har bir jadval belgini o'zining
        // oxirgi qatorida ushlab turadi. Takror yuborish zarar qilmaydi:
        // qo'llash ID bo'yicha va idempotent.
        var caps = new List<DateTimeOffset>();
        if (productDtos.Count >= ProductBatch) caps.Add(productDtos.Max(p => p.UpdatedAt));
        if (customerDtos.Count >= CustomerBatch) caps.Add(customerDtos.Max(c => c.UpdatedAt));
        if (saleDtos.Count >= SaleBatch) caps.Add(saleDtos.Max(s => s.UpdatedAt));
        if (movements.Count >= StockBatch) caps.Add(movements.Max(m => m.UpdatedAt));
        if (cashDtos.Count >= CashBatch) caps.Add(cashDtos.Max(m => m.UpdatedAt));

        var nextSince = caps.Count > 0
            ? caps.Min()
            : stamps.Count > 0 ? stamps.Max() : since;

        return new SyncPullDto(
            now, nextSince, marketDto, userDtos, productDtos, customerDtos, settingsDto,
            saleDtos, itemDtos, paymentDtos, debtDtos, movements,
            returnDtos, returnItemDtos, cashDtos);
    }

    /// <summary>
    /// Bazadan kelgan vaqtni siljishi bilan birga qaytaradi.
    ///
    /// <para>Qiymat ustunda UTC yotadi, lekin <c>Kind</c> provayderga qarab
    /// <c>Unspecified</c> bo'lib kelishi mumkin. Uni shundayligicha
    /// <c>DateTimeOffset</c> ga aylantirish SERVER mintaqasini qo'shib
    /// yuborardi — ya'ni natija serverning sozlamasiga bog'liq bo'lib
    /// qolardi.</para>
    /// </summary>
    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset? AsUtc(DateTime? value) =>
        value is null ? null : AsUtc(value.Value);
}
