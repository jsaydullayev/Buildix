using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Bulutdan do'konga tushadigan hamma narsa.
///
/// <para><b>Nega aniq ro'yxat, entity emas.</b> Do'kon bazasi bulutdagi
/// bilan bir xil sxemaga ega, ya'ni entity'ni to'g'ridan-to'g'ri yuborish
/// oson bo'lardi. Lekin unda har qanday yangi maydon — masalan ichki
/// bayroq yoki boshqa do'konga tegishli havola — o'ylanmagan holda do'konga
/// ketib qolardi. Bu ro'yxat shartnoma: unga nima kirsa, faqat o'sha
/// tushadi.</para>
///
/// <para><b>Nega vaqtlar <see cref="DateTimeOffset"/>.</b> API qolgan hamma
/// joyda vaqtni Toshkent mintaqasida va BELGISIZ yuboradi — interfeys uchun
/// bu qulay. Sinxronizatsiya uchun esa halokatli: do'kon o'zi olgan belgini
/// qaytarganda uning qaysi mintaqada ekani noma'lum bo'lar, Npgsql esa
/// bunday qiymatni UTC ustuni bilan solishtirishdan bosh tortardi (400).
/// Undan ham yomoni — belgi ustunlardan 5 soat oldinda bo'lgani uchun do'kon
/// har sinxronizatsiyada 5 soatlik o'zgarishni JIMGINA o'tkazib yuborardi.
/// <c>DateTimeOffset</c> o'z siljishini o'zi olib yuradi va Toshkent
/// o'zgartirgichi (u <c>DateTime</c> uchun ro'yxatdan o'tgan) unga
/// tegmaydi.</para>
/// </summary>
public record SyncPullDto(
    /// <summary>Bulutning vaqti — do'kon soati bilan solishtirish uchun.</summary>
    [property: JsonPropertyName("serverTimeUtc")] DateTimeOffset ServerTimeUtc,

    /// <summary>Keyingi so'rovda shu qiymat yuboriladi.</summary>
    [property: JsonPropertyName("nextSince")] DateTimeOffset NextSince,

    [property: JsonPropertyName("market")] SyncMarketDto? Market,
    [property: JsonPropertyName("users")] IReadOnlyList<SyncUserDto> Users,

    /// <summary>
    /// Egasi masofadan o'zgartirgan tovar maydonlari.
    /// </summary>
    /// <remarks>
    /// <para>QOLDIQ bu yerda YO'Q va bo'lishi ham mumkin emas. Qoldiqni
    /// faqat do'kon biladi: tovar u yerda jismonan turadi va u yerda
    /// sotiladi. Bulutdagi son esa oxirgi yuborishdagi nusxa — uni do'konga
    /// qaytarish o'sha payt sotilgan tovarni «tirilitib» yuborardi va
    /// kassir omborda yo'q narsani sotishga urinardi.</para>
    ///
    /// <para>Egasi masofadan narxni o'zgartira oladi — bu eng ko'p
    /// so'raladigan amal. Ilgari o'zgarish do'konga HECH QACHON yetib
    /// bormasdi: kassa eski narxda sotaverar, ertasiga esa do'kon o'sha
    /// tovarni yuborib, bulutdagi yangi narxni jimgina eskisiga
    /// almashtirardi.</para>
    /// </remarks>
    ///
    /// <para>Sukut bo'yicha BO'SH: eski do'kon nusxasi bu maydonni umuman
    /// bilmaydi va uni talab qilish o'sha do'konlarning tortishini
    /// yiqitardi.</para>
    [property: JsonPropertyName("products")] IReadOnlyList<SyncProductDto>? Products = null,

    /// <summary>
    /// Egasi paneldan qo'shgan yoki o'zgartirgan mijozlar.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari mijozlar pastga UMUMAN tushmasdi: egasi saytdan mijoz
    /// qo'shsa yoki uning qarz chegarasini o'zgartirsa, do'kon buni hech
    /// qachon bilmasdi.</para>
    ///
    /// <para>Sukut bo'yicha BO'SH: eski do'kon nusxasi bu maydonni bilmaydi
    /// va uni talab qilish o'sha do'konlarning tortishini yiqitardi.</para>
    /// </remarks>
    [property: JsonPropertyName("customers")] IReadOnlyList<SyncCustomerDto>? Customers = null,

    /// <summary>
    /// Do'kon sozlamalari — o'zgargan bo'lsa. Sukut bo'yicha <c>null</c>:
    /// eski do'kon nusxasi bu maydonni bilmaydi.
    /// </summary>
    [property: JsonPropertyName("settings")] SyncSettingsDto? Settings = null,

    /// <summary>
    /// Boshqa kassalarda urilgan cheklar va ularning qatorlari.
    /// </summary>
    /// <remarks>
    /// Sukut bo'yicha bo'sh: eski do'kon nusxasi bu maydonlarni bilmaydi.
    /// </remarks>
    [property: JsonPropertyName("sales")] IReadOnlyList<SyncSaleDto>? Sales = null,
    [property: JsonPropertyName("saleItems")] IReadOnlyList<SyncSaleItemDto>? SaleItems = null,
    [property: JsonPropertyName("payments")] IReadOnlyList<SyncPaymentDto>? Payments = null,
    [property: JsonPropertyName("debts")] IReadOnlyList<SyncDebtDto>? Debts = null,

    /// <summary>
    /// Boshqa kassalardagi ombor harakatlari — o'z kursori bilan.
    /// </summary>
    [property: JsonPropertyName("stockMovements")]
    IReadOnlyList<SyncStockMovementDto>? StockMovements = null)
{
    /// <summary>Tovarlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncProductDto> ProductsOrEmpty => Products ?? [];

    /// <summary>Mijozlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncCustomerDto> CustomersOrEmpty => Customers ?? [];

    /// <summary>Cheklar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncSaleDto> SalesOrEmpty => Sales ?? [];

    /// <summary>Chek qatorlari — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncSaleItemDto> SaleItemsOrEmpty => SaleItems ?? [];

    /// <summary>To'lovlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncPaymentDto> PaymentsOrEmpty => Payments ?? [];

    /// <summary>Qarzlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncDebtDto> DebtsOrEmpty => Debts ?? [];

    /// <summary>Ombor harakatlari — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncStockMovementDto> StockMovementsOrEmpty => StockMovements ?? [];
}

/// <summary>
/// Tovarning EGASI boshqaradigan maydonlari.
///
/// <para>Ro'yxat ataylab qisqa: bu yerda faqat masofadan o'zgartirilishi
/// mantiqiy bo'lgan narsalar. Har qo'shilgan maydon ikki tomondan
/// yozilishi mumkin bo'lgan yana bitta joy demakdir.</para>
/// </summary>
public record SyncProductDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("costPrice")] decimal CostPrice,
    [property: JsonPropertyName("salePrice")] decimal SalePrice,
    [property: JsonPropertyName("minSalePrice")] decimal MinSalePrice,
    [property: JsonPropertyName("minThreshold")] decimal MinThreshold,
    [property: JsonPropertyName("sku")] string? Sku,
    [property: JsonPropertyName("barcode")] string? Barcode,
    [property: JsonPropertyName("isHidden")] bool IsHidden,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt,

    /// <summary>
    /// O'lchov birligi (<see cref="Domain.Enums.UnitType"/> raqami).
    /// </summary>
    /// <remarks>
    /// <para>Do'konda YANGI tovar yaratish uchun kerak: birliksiz u
    /// «dona» bo'lib tushar va qopda sotiladigan sement kassada dona bilan
    /// ko'rinardi. Mavjud tovarni yangilashda ham qo'llanadi — egasi
    /// birlikni panelda to'g'irlashi mumkin.</para>
    ///
    /// <para><b>0 — «yuborilmagan», birlik EMAS.</b> Sanoq 1 dan boshlanadi
    /// (<c>Piece = 1</c>), ya'ni nol hech qanday birlikka to'g'ri kelmaydi va
    /// buni sentinel sifatida ishlatish xavfsiz. Eski bulut bu maydonni
    /// umuman yubormaydi — o'shanda do'kondagi birlik O'Z HOLICHA qoladi.
    /// Agar nol haqiqiy qiymat bo'lganida (masalan <c>Piece = 0</c>), eski
    /// bulut bilan ishlayotgan do'konning har bir tovari jimgina «dona» ga
    /// aylanardi.</para>
    /// </remarks>
    [property: JsonPropertyName("unit")] int Unit = 0);

/// <summary>
/// Mijoz — egasi paneldan boshqaradigan maydonlar.
/// </summary>
/// <remarks>
/// <para><b>QARZ bu yerda YO'Q va bo'lishi ham shart emas.</b> Tovar
/// qoldig'idan farqli o'laroq, mijozning qarzi <see cref="Domain.Entities.Customer"/>
/// da alohida ustun sifatida YOTMAYDI — u <c>Debts</c> qatorlaridan
/// hisoblanadi. Ya'ni qarzni sinxronlash uchun alohida qoida kerak emas:
/// qatorlar push bilan yuqoriga chiqadi va ikkala tomonda ham bir xil
/// hisoblanadi.</para>
///
/// <para><c>DebtLimit</c> esa aksincha — bu egasi qo'yadigan CHEGARA, qarzning
/// o'zi emas. U paneldan o'zgartiriladi va do'konga tushishi kerak.</para>
/// </remarks>
public record SyncCustomerDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("fullName")] string? FullName,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("customerType")] int CustomerType,
    [property: JsonPropertyName("isRegular")] bool IsRegular,
    [property: JsonPropertyName("debtLimit")] decimal? DebtLimit,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>
/// BOSHQA kassada urilgan chek.
/// </summary>
/// <remarks>
/// <para><b>Nima uchun pastga tushadi.</b> Har kassa o'z bazasi bilan
/// ishlaganda 2-kassa 1-kassaning cheklarini KO'RMAYDI — ya'ni boshqa
/// kassada urilgan chekni qaytarib bo'lmaydi va uning qarzini undirib
/// bo'lmaydi. Mijoz uchun bu «chekingiz bizda yo'q» degani.</para>
///
/// <para><b>Qoldiq va kassa balansi bu yerda YO'Q.</b> Ular chekdan emas,
/// ombor va kassa JURNALIDAN hisoblanadi (1-bosqich). Chek bilan birga
/// qoldiqni ko'chirish o'sha jurnalni ikki marta hisoblab yuborardi.</para>
/// </remarks>
public record SyncSaleDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleNumber")] int SaleNumber,
    [property: JsonPropertyName("registerCode")] string? RegisterCode,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("shiftId")] Guid? ShiftId,
    [property: JsonPropertyName("customerId")] Guid? CustomerId,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount,
    [property: JsonPropertyName("paidAmount")] decimal PaidAmount,
    [property: JsonPropertyName("discountAmount")] decimal DiscountAmount,
    [property: JsonPropertyName("isOpeningBalance")] bool IsOpeningBalance,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>Chekning bitta qatori.</summary>
public record SyncSaleItemDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleId")] Guid SaleId,
    [property: JsonPropertyName("productId")] Guid? ProductId,
    [property: JsonPropertyName("isExternal")] bool IsExternal,
    [property: JsonPropertyName("externalProductName")] string? ExternalProductName,
    [property: JsonPropertyName("externalCostPrice")] decimal ExternalCostPrice,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("costPrice")] decimal CostPrice,
    [property: JsonPropertyName("salePrice")] decimal SalePrice,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>
/// Chekning qarzi.
/// </summary>
/// <remarks>
/// <para>Chek bilan BIRGA yuriladi. Qarz o'zgarganda (masalan qisman
/// to'langanda) chekning <c>PaidAmount</c> i ham o'zgaradi, ya'ni otasi
/// baribir qaytadan tushadi va qarz u bilan birga keladi — alohida kursor
/// kerak emas.</para>
/// </remarks>
public record SyncDebtDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleId")] Guid SaleId,
    [property: JsonPropertyName("customerId")] Guid CustomerId,
    [property: JsonPropertyName("totalDebt")] decimal TotalDebt,
    [property: JsonPropertyName("remainingDebt")] decimal RemainingDebt,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("dueDate")] DateTimeOffset? DueDate,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>Chekka yozilgan to'lov (manfiy — qaytarish).</summary>
public record SyncPaymentDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("saleId")] Guid SaleId,
    [property: JsonPropertyName("paymentType")] int PaymentType,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("collectedByUserId")] Guid? CollectedByUserId,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>
/// Ombor harakati — boshqa kassada bo'lgan qoldiq o'zgarishi.
/// </summary>
/// <remarks>
/// <para><b>Nima uchun bu HAL QILUVCHI.</b> Tovar do'konda BITTA uyumda
/// turadi, kassalar esa ikkita. A kassa 3 dona sotsa, B kassa buni bilishi
/// SHART — aks holda u omborda yo'q tovarni sotishga urinadi. Qoldiq
/// ustunini ko'chirib bo'lmaydi (oxirgi yozgan g'olib chiqadi), jurnal esa
/// qo'shiladigan: ikkala kassaning harakatlari shunchaki yig'iladi.</para>
///
/// <para>Chekdan farqli o'laroq bu jadval otasi bilan yurmaydi — harakat
/// tovarga bog'langan va o'z kursoriga ega. Jurnal QO'SHILADIGAN: yozilgan
/// harakat hech qachon o'zgarmaydi.</para>
///
/// <para><c>ResultingQty</c> — harakat yozilgan kassadagi holat. U TARIX
/// uchun ko'chiriladi, lekin qoldiqni hisoblashda ISHLATILMAYDI: boshqa
/// kassada o'sha payt boshqa son bo'lgan.</para>
/// </remarks>
public record SyncStockMovementDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("productId")] Guid ProductId,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("delta")] decimal Delta,
    [property: JsonPropertyName("resultingQty")] decimal ResultingQty,
    [property: JsonPropertyName("refNumber")] int? RefNumber,
    [property: JsonPropertyName("userId")] Guid? UserId,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>
/// Do'kon sozlamalari — egasi paneldan boshqaradigan qoidalar.
/// </summary>
/// <remarks>
/// <para><b>Ilgari sozlamalar UMUMAN sinxronlanmasdi</b> — na yuqoriga, na
/// pastga. Ya'ni bulutda va do'konda ikkita mustaqil nusxa yotardi va ular
/// hech qachon uchrashmasdi: egasi saytda do'kon manzilini yozsa, chekda u
/// paydo bo'lmasdi; chek enini 58 mm qilsa, kassa 80 mm bosaverardi. Hech
/// qanday xato chiqmasdi — sozlama «saqlandi» deb yozar, faqat boshqa
/// nusxaga tegmasdi.</para>
///
/// <para>Faqat DO'KON ishlatadigan maydonlar. Bildirishnoma bayroqlari va
/// yuborilgan xulosa sanasi bu yerda yo'q: ular bulutning o'z ishi.</para>
/// </remarks>
public record SyncSettingsDto(
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("workingHours")] string? WorkingHours,

    [property: JsonPropertyName("receiptHeader")] string? ReceiptHeader,
    [property: JsonPropertyName("receiptFooter")] string? ReceiptFooter,
    [property: JsonPropertyName("autoPrintReceipt")] bool AutoPrintReceipt,
    [property: JsonPropertyName("receiptWidthMm")] int ReceiptWidthMm,

    [property: JsonPropertyName("salesOnlyWhenShiftOpen")] bool SalesOnlyWhenShiftOpen,
    [property: JsonPropertyName("cashWithdrawalNeedsApproval")] bool CashWithdrawalNeedsApproval,
    [property: JsonPropertyName("debtOnlyForRegulars")] bool DebtOnlyForRegulars,
    [property: JsonPropertyName("debtRequiresCloud")] bool DebtRequiresCloud,
    [property: JsonPropertyName("defaultDebtLimit")] decimal DefaultDebtLimit,
    [property: JsonPropertyName("blockSaleBelowCost")] bool BlockSaleBelowCost,

    [property: JsonPropertyName("allowedCashDiscrepancy")] decimal AllowedCashDiscrepancy,
    [property: JsonPropertyName("minStockAlertEnabled")] bool MinStockAlertEnabled,
    [property: JsonPropertyName("defaultMarkupPct")] decimal DefaultMarkupPct,
    [property: JsonPropertyName("inactivityLogoutMinutes")] int InactivityLogoutMinutes,
    [property: JsonPropertyName("auditEnabled")] bool AuditEnabled,

    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>Do'konning o'zi. Obuna holati shu maydonlardan hisoblanadi.</summary>
public record SyncMarketDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    /// <summary>
    /// Do'konning manzildagi nomi (<c>/taxtapul/...</c>).
    ///
    /// <para><b>Nega u sinxronizatsiyada.</b> Kirishdan keyin interfeys
    /// AYNAN shu qiymat bo'yicha ish ekraniga o'tadi. Do'kon nusxasida u
    /// bo'sh bo'lsa, kirish muvaffaqiyatli o'tadi-yu, sahifa joyidan
    /// qimirlamaydi: na o'tish, na xato — kassir «tugma ishlamayapti»
    /// degandan boshqa hech narsa ko'rmaydi.</para>
    /// </summary>
    [property: JsonPropertyName("subdomain")] string? Subdomain,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("isBlocked")] bool IsBlocked,
    [property: JsonPropertyName("blockedReason")] string? BlockedReason,
    [property: JsonPropertyName("ownerId")] Guid OwnerId,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

/// <summary>
/// Xodim.
///
/// <para><b>Parol hash'i ATAYLAB yuboriladi.</b> Do'kon internetsiz ishlashi
/// kerak, ya'ni kirishni o'zi tekshiradi — buning uchun hash undan boshqa
/// yo'l yo'q. Hash bulutda ham shu ko'rinishda yotadi va undan parolni
/// tiklab bo'lmaydi. Aynan shu sababli kanal HTTPS bo'lishi SHART.</para>
/// </summary>
public record SyncUserDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("passwordHash")] string PasswordHash,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("role")] int Role,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("isDeleted")] bool IsDeleted,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions,
    [property: JsonPropertyName("isPermissionsCustomized")] bool IsPermissionsCustomized,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("maxDebtPerCheck")] decimal? MaxDebtPerCheck,
    [property: JsonPropertyName("maxDiscountPercent")] int? MaxDiscountPercent,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);
