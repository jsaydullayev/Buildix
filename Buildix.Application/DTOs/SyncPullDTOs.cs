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
    [property: JsonPropertyName("users")] IReadOnlyList<SyncUserDto> Users);

/// <summary>Do'konning o'zi. Obuna holati shu maydonlardan hisoblanadi.</summary>
public record SyncMarketDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
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
