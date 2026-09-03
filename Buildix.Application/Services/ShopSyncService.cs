using System.Net.Http.Json;
using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Common;
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

            // 5. Mijozlar — xuddi shu qoida bo'yicha.
            var customerCount = await ApplyCustomersAsync(payload.CustomersOrEmpty, marketId, ct);

            // 6. Do'kon sozlamalari.
            var settingsChanged = await ApplySettingsAsync(payload.Settings, marketId, ct);

            // 7. Boshqa kassalarda urilgan cheklar — qatorlari va to'lovlari
            //    bilan. Eng oxirida: ular xodim, tovar va mijozga ishora
            //    qiladi, ya'ni o'shalar allaqachon yozilgan bo'lishi kerak.
            var (saleCount, deferredFrom) = await ApplySalesAsync(payload, marketId, ct);

            state ??= NewState(marketId);
            // Kutilgan chek bo'lsa, belgi undan O'TIB KETMAYDI — aks holda
            // u boshqa hech qachon so'ralmasdi va chek abadiy yo'qolardi.
            state.PullWatermark = deferredFrom ?? payload.NextSince;
            state.LastPulledAtUtc = _clock.GetUtcNow().UtcDateTime;
            state.LastError = null;

            await _unitOfWork.SaveChangesAsync(ct);

            // Belgilar SAQLASHDAN KEYIN yoziladi: qatorning yakuniy
            // `UpdatedAt` i aynan o'sha saqlashda qo'yiladi va belgi shu
            // qiymatga tayanadi.
            await MarkSyncedAsync(ct);

            _logger.LogInformation(
                "Cloud pull applied: market={MarketChanged} users={UserCount} products={ProductCount} "
                + "customers={CustomerCount} settings={SettingsChanged} sales={SaleCount} "
                + "watermark={Watermark:O}",
                payload.Market is not null, payload.Users.Count, productCount, customerCount,
                settingsChanged, saleCount, state.PullWatermark);

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
    /// <para><b>Yangi tovar YARATILADI ham.</b> Ilgari noma'lum id shunchaki
    /// o'tkazib yuborilardi va bu jimgina teshik edi: egasi katalogga
    /// saytdan tovar qo'shsa, u do'konga HECH QACHON yetib bormasdi.
    /// Xato chiqmasdi, hech qayerga yozilmasdi — egasi tovarni panelda
    /// ko'rar, kassir esa uni kassadan topa olmasdi.</para>
    ///
    /// <para><b>Yangi tovarning qoldig'i — NOL.</b> Bulut do'kondagi
    /// qoldiqni bilmaydi va bilishi ham mumkin emas (yuqoriga qarang).
    /// Tovar do'konga kelganda qoldiq xaridnoma yoki inventarizatsiya
    /// orqali paydo bo'ladi. Egasi saytda miqdor yozgan bo'lsa ham, u
    /// do'konga o'tmaydi — aks holda omborda yo'q tovar bor bo'lib
    /// ko'rinardi.</para>
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
            if (!local.TryGetValue(dto.Id, out var product))
            {
                // Saytdan qo'shilgan tovar. Id ATAYLAB bulutdagidek: aks
                // holda keyingi push do'kon nusxasini ikkinchi tovar deb
                // yozar va katalogda juftlik paydo bo'lardi.
                product = new Product
                {
                    Id = dto.Id,
                    MarketId = marketId,
                    // Qoldiq NOL: uni faqat do'kon biladi (izohga qarang).
                    Quantity = 0m,
                };
                _context.Products.Add(product);
                local[dto.Id] = product;
            }
            else
            {
                // Do'kondagi yozuv yangiroq bo'lsa — tegmaymiz. Yangi
                // yaratilganda solishtiradigan narsa yo'q.
                var cloudTime = dto.UpdatedAt.UtcDateTime;
                if (DateTime.SpecifyKind(product.UpdatedAt, DateTimeKind.Utc) > cloudTime) continue;
            }

            product.Unit = Enum.IsDefined(typeof(UnitType), dto.Unit)
                ? (UnitType)dto.Unit
                : product.Unit;
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
    /// Egasi paneldan qo'shgan yoki o'zgartirgan mijozlarni qo'llaydi.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari mijozlar pastga UMUMAN tushmasdi: egasi saytdan mijoz
    /// qo'shsa yoki uning qarz chegarasini o'zgartirsa, do'kon buni hech
    /// qachon bilmasdi. Kassir o'sha mijozni kassadan topa olmasdi va uni
    /// QAYTA yaratardi — natijada bitta odam bazada ikkita mijoz bo'lib,
    /// qarzi ikkiga bo'linib ketardi.</para>
    ///
    /// <para><b>Qarz KO'CHIRILMAYDI</b> — chunki u ustun emas: mijozning
    /// qarzi <c>Debts</c> qatorlaridan hisoblanadi va o'sha qatorlar push
    /// bilan yuqoriga chiqadi. Tovar qoldig'idagi kabi maxsus qoida shu
    /// sababdan kerak emas.</para>
    ///
    /// <para>Vaqt solishtiruvi tovarlardagi bilan bir xil: do'kondagi yozuv
    /// yangiroq bo'lsa, uni eski nusxa bilan bosib yuborish mumkin emas.</para>
    /// </remarks>
    private async Task<int> ApplyCustomersAsync(
        IReadOnlyList<SyncCustomerDto> incoming, int marketId, CancellationToken ct)
    {
        if (incoming.Count == 0) return 0;

        var ids = incoming.Select(c => c.Id).ToList();
        var local = await _context.Customers
            .IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id) && c.MarketId == marketId)
            .ToDictionaryAsync(c => c.Id, ct);

        var applied = 0;
        foreach (var dto in incoming)
        {
            if (!local.TryGetValue(dto.Id, out var customer))
            {
                // Id ATAYLAB bulutdagidek — aks holda keyingi push do'kon
                // nusxasini ikkinchi mijoz deb yozardi.
                customer = new Customer { Id = dto.Id, MarketId = marketId };
                _context.Customers.Add(customer);
                local[dto.Id] = customer;
            }
            else
            {
                var cloudTime = dto.UpdatedAt.UtcDateTime;
                if (DateTime.SpecifyKind(customer.UpdatedAt, DateTimeKind.Utc) > cloudTime) continue;
            }

            customer.Phone = dto.Phone;
            customer.FullName = dto.FullName;
            customer.Comment = dto.Comment;
            customer.CustomerType = Enum.IsDefined(typeof(CustomerType), dto.CustomerType)
                ? (CustomerType)dto.CustomerType
                : customer.CustomerType;
            customer.IsRegular = dto.IsRegular;
            customer.DebtLimit = dto.DebtLimit;
            customer.IsDeleted = dto.IsDeleted;
            // Qarz ATAYLAB yo'q — u ustun emas (izohga qarang).

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Egasi paneldan o'zgartirgan do'kon sozlamalarini qo'llaydi.
    /// </summary>
    /// <remarks>
    /// <para><b>Ilgari sozlamalar UMUMAN sinxronlanmasdi.</b> Bulutda va
    /// do'konda ikkita mustaqil nusxa yotardi: egasi saytda do'kon manzilini
    /// yozsa chekda u paydo bo'lmasdi, chek enini 58 mm qilsa kassa 80 mm
    /// bosaverardi. Sozlama «saqlandi» deb yozar, faqat boshqa nusxaga
    /// tegmasdi — ya'ni xato hech qayerda ko'rinmasdi.</para>
    ///
    /// <para><b>Yangiroq nusxa g'olib</b> — tovar va mijozdagi bilan bir xil
    /// qoida. Do'konda ham sozlash ekrani bor va u yerdagi o'zgarishni eski
    /// bulut nusxasi bilan bosib yuborish mumkin emas.</para>
    ///
    /// <para><b>Qolgan kamchilik:</b> do'konda qilingan o'zgarish bulutga
    /// CHIQMAYDI — sozlamalar hali push'ga qo'shilmagan. Ya'ni egasi
    /// telefonda ko'radigan sozlamalar do'konnikidan orqada qolishi mumkin.
    /// Buni tuzatish uchun push tomoni ham kerak.</para>
    /// </remarks>
    private async Task<bool> ApplySettingsAsync(
        SyncSettingsDto? dto, int marketId, CancellationToken ct)
    {
        if (dto is null) return false;

        var settings = await _context.MarketSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.MarketId == marketId, ct);

        if (settings is null)
        {
            settings = new MarketSettings { MarketId = marketId };
            _context.MarketSettings.Add(settings);
        }
        else if (DateTime.SpecifyKind(settings.UpdatedAt, DateTimeKind.Utc) > dto.UpdatedAt.UtcDateTime)
        {
            // Do'kondagi o'zgarish yangiroq — tegmaymiz.
            return false;
        }

        settings.Phone = dto.Phone;
        settings.Address = dto.Address;
        settings.WorkingHours = dto.WorkingHours;

        settings.ReceiptHeader = dto.ReceiptHeader;
        settings.ReceiptFooter = dto.ReceiptFooter;
        settings.AutoPrintReceipt = dto.AutoPrintReceipt;
        settings.ReceiptWidthMm = dto.ReceiptWidthMm;

        settings.SalesOnlyWhenShiftOpen = dto.SalesOnlyWhenShiftOpen;
        settings.CashWithdrawalNeedsApproval = dto.CashWithdrawalNeedsApproval;
        settings.DebtOnlyForRegulars = dto.DebtOnlyForRegulars;
        settings.DebtRequiresCloud = dto.DebtRequiresCloud;
        settings.DefaultDebtLimit = dto.DefaultDebtLimit;
        settings.BlockSaleBelowCost = dto.BlockSaleBelowCost;

        settings.AllowedCashDiscrepancy = dto.AllowedCashDiscrepancy;
        settings.MinStockAlertEnabled = dto.MinStockAlertEnabled;
        settings.DefaultMarkupPct = dto.DefaultMarkupPct;
        settings.InactivityLogoutMinutes = dto.InactivityLogoutMinutes;
        settings.AuditEnabled = dto.AuditEnabled;

        return true;
    }

    /// <summary>
    /// Shu tortishda qo'llangan qatorlar — belgi ular uchun yoziladi.
    /// </summary>
    private readonly List<(Guid Id, string Table, BaseEntity Row)> _applied = [];

    /// <summary>
    /// Boshqa kassalarda urilgan cheklarni qatorlari va to'lovlari bilan
    /// qo'llaydi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nima uchun.</b> Har kassa o'z bazasi bilan ishlaganda
    /// 2-kassa 1-kassaning cheklarini ko'rmaydi — boshqa kassada urilgan
    /// chekni qaytarib ham, uning qarzini undirib ham bo'lmaydi. Mijoz uchun
    /// bu «chekingiz bizda yo'q» degani.</para>
    ///
    /// <para><b>Notanish xodimli chek KUTILADI.</b> Chek sotuvchiga ishora
    /// qiladi va u hali tushmagan bo'lishi mumkin. Bunday chekni yozish
    /// tashqi kalitni buzardi, tashlab yuborish esa uni abadiy yo'qotardi —
    /// shuning uchun u chetga qo'yiladi va suv belgisi undan o'tib
    /// ketmaydi: keyingi tortishda u qaytadan keladi.</para>
    ///
    /// <para><b>Smena bog'lanishi UZILADI.</b> Smenalar pastga tushmaydi,
    /// ya'ni begona <c>ShiftId</c> tashqi kalitni buzardi. Yo'qotiladigan
    /// narsa — chekdagi «Смена №N» yozuvi, u ham faqat o'z kassasida
    /// ma'noga ega.</para>
    /// </remarks>
    private async Task<(int Applied, DateTimeOffset? DeferredFrom)> ApplySalesAsync(
        SyncPullDto payload, int marketId, CancellationToken ct)
    {
        var incoming = payload.SalesOrEmpty;
        if (incoming.Count == 0) return (0, null);

        var ids = incoming.Select(s => s.Id).ToList();
        var local = await _context.Sales
            .IgnoreQueryFilters()
            .Where(s => ids.Contains(s.Id) && s.MarketId == marketId)
            .ToDictionaryAsync(s => s.Id, ct);

        // Mavjud xodimlar — chek faqat tanish sotuvchi bilan yoziladi.
        var sellerIds = incoming.Select(s => s.SellerId).Distinct().ToList();
        var knownSellers = (await _context.Users
            .IgnoreQueryFilters()
            .Where(u => sellerIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet();

        // Mijoz ixtiyoriy, lekin mavjud bo'lishi shart — aks holda havola
        // buziladi. Noma'lum mijoz chekni KUTDIRMAYDI: uni bo'sh qoldirish
        // chekni butunlay yo'qotishdan yaxshiroq.
        var customerIds = incoming.Where(s => s.CustomerId.HasValue)
            .Select(s => s.CustomerId!.Value).Distinct().ToList();
        var knownCustomers = (await _context.Customers
            .IgnoreQueryFilters()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct)).ToHashSet();

        var accepted = new HashSet<Guid>();
        DateTimeOffset? deferredFrom = null;
        var applied = 0;

        foreach (var dto in incoming)
        {
            if (!knownSellers.Contains(dto.SellerId))
            {
                // Sotuvchi hali kelmagan — chekni kutamiz.
                deferredFrom = deferredFrom is { } d && d <= dto.UpdatedAt ? d : dto.UpdatedAt;
                continue;
            }

            if (!local.TryGetValue(dto.Id, out var sale))
            {
                sale = new Sale { Id = dto.Id, MarketId = marketId };
                _context.Sales.Add(sale);
                local[dto.Id] = sale;
            }
            else if (DateTime.SpecifyKind(sale.UpdatedAt, DateTimeKind.Utc) > dto.UpdatedAt.UtcDateTime)
            {
                // Do'kondagi nusxa yangiroq — tegmaymiz, lekin bolalari
                // baribir qo'llanadi (ular ham do'kondagidan eski bo'lsa
                // o'z navbatida o'tkazib yuboriladi).
                accepted.Add(dto.Id);
                continue;
            }

            sale.SaleNumber = dto.SaleNumber;
            sale.RegisterCode = dto.RegisterCode;
            sale.SellerId = dto.SellerId;
            sale.ShiftId = null;   // smenalar pastga tushmaydi (izohga qarang)
            sale.CustomerId = dto.CustomerId is { } cid && knownCustomers.Contains(cid) ? cid : null;
            sale.Status = Enum.IsDefined(typeof(SaleStatus), dto.Status)
                ? (SaleStatus)dto.Status
                : sale.Status;
            sale.TotalAmount = dto.TotalAmount;
            sale.PaidAmount = dto.PaidAmount;
            sale.DiscountAmount = dto.DiscountAmount;
            sale.IsOpeningBalance = dto.IsOpeningBalance;
            sale.IsDeleted = dto.IsDeleted;
            sale.CreatedAt = dto.CreatedAt.UtcDateTime;

            accepted.Add(dto.Id);
            _applied.Add((sale.Id, nameof(Sale), sale));
            applied++;
        }

        if (accepted.Count > 0)
        {
            await ApplySaleItemsAsync(payload.SaleItemsOrEmpty, accepted, ct);
            await ApplyPaymentsAsync(payload.PaymentsOrEmpty, accepted, marketId, ct);

            // Qarz mijozsiz bo'lolmaydi (havola majburiy). Mijoz hali
            // kelmagan bo'lsa qarz KUTILADI — chunki uni tashlab yuborish
            // «chek bor, qarz yo'q» degan holatga olib kelardi va mijozning
            // qarzi jimgina yo'qolardi.
            var debtDeferred = await ApplyDebtsAsync(
                payload.DebtsOrEmpty, accepted, marketId, ct);
            if (debtDeferred is { } dd && (deferredFrom is not { } cur || dd < cur))
                deferredFrom = dd;
        }

        return (applied, deferredFrom);
    }

    /// <summary>
    /// Chek qatorlari. Faqat QABUL QILINGAN cheklarniki — otasiz qator
    /// tashqi kalitni buzardi.
    /// </summary>
    private async Task ApplySaleItemsAsync(
        IReadOnlyList<SyncSaleItemDto> incoming, HashSet<Guid> acceptedSales, CancellationToken ct)
    {
        var mine = incoming.Where(i => acceptedSales.Contains(i.SaleId)).ToList();
        if (mine.Count == 0) return;

        var ids = mine.Select(i => i.Id).ToList();
        var local = await _context.SaleItems
            .IgnoreQueryFilters()
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        // Tovar hali kelmagan bo'lishi mumkin. Qatorni tashlab yubormaymiz:
        // chekdagi summa tovarsiz ham to'g'ri — havola bo'sh qoladi va
        // keyingi tortishda tovar kelganda to'ldiriladi.
        var productIds = mine.Where(i => i.ProductId.HasValue)
            .Select(i => i.ProductId!.Value).Distinct().ToList();
        var knownProducts = (await _context.Products
            .IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct)).ToHashSet();

        foreach (var dto in mine)
        {
            if (!local.TryGetValue(dto.Id, out var item))
            {
                item = new SaleItem { Id = dto.Id, SaleId = dto.SaleId };
                _context.SaleItems.Add(item);
            }
            else if (DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc) > dto.UpdatedAt.UtcDateTime)
            {
                continue;
            }

            item.ProductId = dto.ProductId is { } pid && knownProducts.Contains(pid) ? pid : null;
            item.IsExternal = dto.IsExternal;
            item.ExternalProductName = dto.ExternalProductName;
            item.ExternalCostPrice = dto.ExternalCostPrice;
            item.Quantity = dto.Quantity;
            item.CostPrice = dto.CostPrice;
            item.SalePrice = dto.SalePrice;
            item.Comment = dto.Comment;

            _applied.Add((item.Id, nameof(SaleItem), item));
        }
    }

    /// <summary>Chekka yozilgan to'lovlar (manfiy — qaytarish).</summary>
    private async Task ApplyPaymentsAsync(
        IReadOnlyList<SyncPaymentDto> incoming, HashSet<Guid> acceptedSales,
        int marketId, CancellationToken ct)
    {
        var mine = incoming.Where(p => acceptedSales.Contains(p.SaleId)).ToList();
        if (mine.Count == 0) return;

        var ids = mine.Select(p => p.Id).ToList();
        var local = await _context.Payments
            .IgnoreQueryFilters()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // Pulni YIQQAN xodim boshqa kassaniki bo'lishi mumkin va u hali
        // kelmagan bo'lishi mumkin — havola bo'sh qoldiriladi.
        var collectorIds = mine.Where(p => p.CollectedByUserId.HasValue)
            .Select(p => p.CollectedByUserId!.Value).Distinct().ToList();
        var knownCollectors = (await _context.Users
            .IgnoreQueryFilters()
            .Where(u => collectorIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet();

        foreach (var dto in mine)
        {
            if (!local.TryGetValue(dto.Id, out var payment))
            {
                payment = new Payment { Id = dto.Id, SaleId = dto.SaleId, MarketId = marketId };
                _context.Payments.Add(payment);
            }
            else if (DateTime.SpecifyKind(payment.UpdatedAt, DateTimeKind.Utc) > dto.UpdatedAt.UtcDateTime)
            {
                continue;
            }

            payment.PaymentType = Enum.IsDefined(typeof(PaymentType), dto.PaymentType)
                ? (PaymentType)dto.PaymentType
                : payment.PaymentType;
            payment.Amount = dto.Amount;
            payment.CollectedByUserId =
                dto.CollectedByUserId is { } uid && knownCollectors.Contains(uid) ? uid : null;
            payment.CreatedAt = dto.CreatedAt.UtcDateTime;

            _applied.Add((payment.Id, nameof(Payment), payment));
        }
    }

    /// <summary>
    /// Chekning qarzi.
    /// </summary>
    /// <remarks>
    /// <para>Qarz MIJOZSIZ bo'lolmaydi — havola majburiy. Mijoz hali
    /// tushmagan bo'lsa qarz kutiladi va suv belgisi undan o'tib ketmaydi:
    /// tashlab yuborish «chek bor, qarz yo'q» holatiga olib kelardi va
    /// mijozning qarzi JIMGINA yo'qolardi — buni keyin hech narsa
    /// ko'rsatmasdi.</para>
    ///
    /// <para>Qarz o'zgarganda (qisman to'langanda) chekning <c>PaidAmount</c>
    /// i ham o'zgaradi, ya'ni otasi qaytadan tushadi va qarz u bilan birga
    /// keladi.</para>
    /// </remarks>
    private async Task<DateTimeOffset?> ApplyDebtsAsync(
        IReadOnlyList<SyncDebtDto> incoming, HashSet<Guid> acceptedSales,
        int marketId, CancellationToken ct)
    {
        var mine = incoming.Where(d => acceptedSales.Contains(d.SaleId)).ToList();
        if (mine.Count == 0) return null;

        var ids = mine.Select(d => d.Id).ToList();
        var local = await _context.Debts
            .IgnoreQueryFilters()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);

        var customerIds = mine.Select(d => d.CustomerId).Distinct().ToList();
        var knownCustomers = (await _context.Customers
            .IgnoreQueryFilters()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct)).ToHashSet();

        DateTimeOffset? deferred = null;

        foreach (var dto in mine)
        {
            if (!knownCustomers.Contains(dto.CustomerId))
            {
                deferred = deferred is { } d && d <= dto.UpdatedAt ? d : dto.UpdatedAt;
                continue;
            }

            if (!local.TryGetValue(dto.Id, out var debt))
            {
                debt = new Debt { Id = dto.Id, SaleId = dto.SaleId, MarketId = marketId };
                _context.Debts.Add(debt);
            }
            else if (DateTime.SpecifyKind(debt.UpdatedAt, DateTimeKind.Utc) > dto.UpdatedAt.UtcDateTime)
            {
                continue;
            }

            debt.CustomerId = dto.CustomerId;
            debt.TotalDebt = dto.TotalDebt;
            debt.RemainingDebt = dto.RemainingDebt;
            debt.Status = Enum.IsDefined(typeof(DebtStatus), dto.Status)
                ? (DebtStatus)dto.Status
                : debt.Status;
            debt.DueDate = dto.DueDate?.UtcDateTime;

            _applied.Add((debt.Id, nameof(Debt), debt));
        }

        return deferred;
    }

    /// <summary>
    /// Qo'llangan qatorlarni «bulutdan keldi» deb belgilaydi.
    /// </summary>
    /// <remarks>
    /// <para>Busiz qator do'kon bazasiga yozilgani zahoti «yangi o'zgargan»
    /// bo'lib ko'rinar va qaytib bulutga ketardi — u yerdan yana pastga,
    /// yana yuqoriga: CHEKSIZ AYLANISH. Belgi qatorning AYNAN shu holatini
    /// eslaydi, ya'ni keyinchalik do'konda qilingan o'zgarish odatdagidek
    /// yuqoriga chiqadi.</para>
    /// </remarks>
    private async Task MarkSyncedAsync(CancellationToken ct)
    {
        if (_applied.Count == 0) return;

        var ids = _applied.Select(x => x.Id).ToList();
        var existing = await _context.SyncedRowMarks
            .Where(m => ids.Contains(m.RowId))
            .ToDictionaryAsync(m => m.RowId, ct);

        foreach (var (id, table, row) in _applied)
        {
            if (existing.TryGetValue(id, out var mark))
            {
                mark.AppliedUpdatedAt = row.UpdatedAt;
                continue;
            }

            _context.SyncedRowMarks.Add(new SyncedRowMark
            {
                RowId = id,
                TableName = table,
                AppliedUpdatedAt = row.UpdatedAt,
            });
        }

        _applied.Clear();
        await _unitOfWork.SaveChangesAsync(ct);
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
