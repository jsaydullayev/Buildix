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
            .Take(500)
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
            .Take(500)
            .ToListAsync(ct);

        var customerDtos = customers.Select(c => new SyncCustomerDto(
            c.Id, c.Phone, c.FullName, c.Comment, (int)c.CustomerType,
            c.IsRegular, c.DebtLimit, c.IsDeleted, AsUtc(c.UpdatedAt))).ToList();

        // Keyingi suv belgisi — QAYTARILGAN yozuvlarning eng kattasi, bulut
        // soati emas. Bulut vaqti olinsa, so'rov bajarilayotgan payt yozilgan
        // yozuv o'tkazib yuborilardi: uning vaqti belgidan kichik bo'lib
        // qolar, lekin u javobga tushmagan bo'lardi.
        var stamps = userDtos.Select(u => u.UpdatedAt)
            .Concat(productDtos.Select(p => p.UpdatedAt))
            .Concat(customerDtos.Select(c => c.UpdatedAt))
            .ToList();
        if (marketDto is not null) stamps.Add(marketDto.UpdatedAt);
        var nextSince = stamps.Count > 0 ? stamps.Max() : since;

        return new SyncPullDto(now, nextSince, marketDto, userDtos, productDtos, customerDtos);
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
