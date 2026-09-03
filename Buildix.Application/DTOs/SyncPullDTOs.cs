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
    [property: JsonPropertyName("customers")] IReadOnlyList<SyncCustomerDto>? Customers = null)
{
    /// <summary>Tovarlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncProductDto> ProductsOrEmpty => Products ?? [];

    /// <summary>Mijozlar — hech qachon <c>null</c> emas.</summary>
    public IReadOnlyList<SyncCustomerDto> CustomersOrEmpty => Customers ?? [];
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
