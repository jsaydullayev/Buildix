using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Do'konning BIRINCHI to'ldirilishi uchun bulutdan olinadigan nusxa.
///
/// <para><b>Nega oddiy tortish yetmaydi.</b> <see cref="SyncPullDto"/>
/// «bulutda nima o'zgardi» degan savolga javob beradi va uning tarkibi
/// ataylab qisqa: do'kon, xodimlar va tovarlarning egasi boshqaradigan
/// maydonlari. Savdolar, qoldiqlar, mijozlar, qarzlar esa do'konda
/// yaratiladi va YUQORIGA yuboriladi — pastga tushadigan yo'l umuman
/// yo'q edi.</para>
///
/// <para>Bu do'kon avval webda ishlab, keyin desktopga o'tganda ko'rinadi:
/// ilova ochiladi, kirish o'tadi, lekin ekran BO'SH. Ma'lumot bulutda
/// turibdi, do'konda esa yo'q va uni olib kelish imkoni ham yo'q.</para>
///
/// <para><b>Nega bo'lak-bo'lak.</b> Uch yillik do'konda yuz minglab qator
/// bo'ladi va ularni bitta javobga solish do'kon xotirasini ham, aloqani
/// ham ko'tarmaydi. Har so'rov bitta jadvalning bir bo'lagini oladi,
/// do'kon esa qayerda to'xtaganini bazasida saqlaydi — uzilgan aloqa
/// nusxani noldan boshlashga majbur qilmaydi.</para>
/// </summary>
public record SyncSnapshotDto(
    /// <summary>Shu javobdagi jadval nomi.</summary>
    [property: JsonPropertyName("table")] string Table,

    /// <summary>
    /// Keyingi so'rovda yuboriladigan joy belgisi. <c>null</c> — bu jadval
    /// tugadi.
    /// </summary>
    [property: JsonPropertyName("nextAfter")] string? NextAfter,

    /// <summary>Jadvaldagi JAMI qator soni — do'kon to'liq olganini tekshiradi.</summary>
    [property: JsonPropertyName("total")] int Total,

    /// <summary>
    /// Qatorlarning o'zi. Faqat <see cref="Table"/> ga tegishli ro'yxat
    /// to'ldiriladi, qolganlari bo'sh keladi.
    /// </summary>
    /// <remarks>
    /// Yuborish to'plami ATAYLAB qayta ishlatiladi: jadvallar ro'yxati,
    /// ularning tartibi va sim formati u yerda allaqachon aniqlangan.
    /// Ikkinchi shartnoma yozilsa, ikkalasi vaqt o'tib bir-biridan
    /// ajralib ketardi — va bu jimgina, faqat ba'zi jadvallar tushmay
    /// qo'ygani bilan bilinardi.
    /// </remarks>
    [property: JsonPropertyName("data")] SyncPushDto Data);

/// <summary>
/// Nusxa olinadigan jadvallar — TASHQI KALIT tartibida.
///
/// <para>Tartib buzilsa yozish tashqi kalitga urilib to'xtaydi: mijozsiz
/// sotuv, sotuvsiz qator yozib bo'lmaydi. Do'kon ro'yxatni aynan shu
/// ketma-ketlikda bo'shatadi.</para>
///
/// <para>Do'kon va xodimlar bu yerda YO'Q — ular oddiy tortishda keladi
/// va nusxa boshlanishidan oldin allaqachon joyida bo'ladi.</para>
/// </summary>
public static class SnapshotTables
{
    public const string ProductCategories = "ProductCategories";
    public const string Suppliers = "Suppliers";
    public const string Customers = "Customers";
    public const string Products = "Products";
    public const string Shifts = "Shifts";
    public const string Sales = "Sales";
    public const string SaleItems = "SaleItems";
    public const string Payments = "Payments";
    public const string Debts = "Debts";
    public const string SaleReturns = "SaleReturns";
    public const string SaleReturnItems = "SaleReturnItems";
    public const string ZakupReceipts = "ZakupReceipts";
    public const string Zakups = "Zakups";
    public const string CashMovements = "CashMovements";
    public const string StockMovements = "StockMovements";

    /// <summary>Bo'shatish tartibi.</summary>
    public static readonly IReadOnlyList<string> InOrder =
    [
        ProductCategories,
        Suppliers,
        Customers,
        Products,
        Shifts,
        Sales,
        SaleItems,
        Payments,
        Debts,
        SaleReturns,
        SaleReturnItems,
        ZakupReceipts,
        Zakups,
        CashMovements,
        StockMovements,
    ];
}
