using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Warehouse KPI tiles, computed DB-side. Replaces the old client rollup that
/// downloaded every product just to count four numbers — O(catalogue) per page
/// load. <see cref="StockValue"/> is null for callers without data.costPrice
/// (cost is masked), so the UI can hide the tile rather than show 0.
/// </summary>
public record WarehouseSummaryDto(
    [property: JsonPropertyName("positions")] int Positions,
    [property: JsonPropertyName("stockValue")] decimal? StockValue,
    [property: JsonPropertyName("lowStock")] int LowStock,
    [property: JsonPropertyName("outOfStock")] int OutOfStock
);

public record ProductDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("costPrice")] decimal CostPrice,
    [property: JsonPropertyName("salePrice")] decimal SalePrice,
    [property: JsonPropertyName("minSalePrice")] decimal MinSalePrice,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("minThreshold")] decimal MinThreshold,
    [property: JsonPropertyName("unit")] int Unit,
    [property: JsonPropertyName("unitName")] string UnitName,
    [property: JsonPropertyName("categoryId")] int? CategoryId,
    [property: JsonPropertyName("categoryName")] string? CategoryName,
    [property: JsonPropertyName("isTemporary")] bool IsTemporary,
    [property: JsonPropertyName("isInStock")] bool IsInStock,
    [property: JsonPropertyName("isLowStock")] bool IsLowStock,
    // Server-nisbiy URL yoki null. Faqat savdo (POS) ekranida ko'rsatish uchun.
    [property: JsonPropertyName("imageUrl")] string? ImageUrl = null,
    // True bo'lsa — bu mahsulot narxi POS oqimida Seller roliga yashiriladi
    // (klient tomonida gate qilinadi). Mahsulotlar bo'limida narx baribir ko'rinadi.
    [property: JsonPropertyName("hidePriceFromSellers")] bool HidePriceFromSellers = false,
    // Artikul / SKU (ixtiyoriy) — Склад ekranida "АРТИКУЛ" ustuni + qidiruv.
    [property: JsonPropertyName("sku")] string? Sku = null,
    // Sotuvchiga ko'rinadigan tavsif (Товары ekrani "Описание").
    [property: JsonPropertyName("description")] string? Description = null,
    // True — POS/sotuvchi katalogidan yashirilgan (hisobotlarda qoladi).
    [property: JsonPropertyName("isHidden")] bool IsHidden = false,
    // Omborda saqlash joyi ("МЕСТО" ustuni + kartochka).
    [property: JsonPropertyName("warehouseLocation")] string? WarehouseLocation = null,
    // Склад «ПОСЛ. ПРИХОД» ustuni — bu tovar bo'yicha oxirgi qabul qilingan
    // postavka sanasi + chek raqami. Ro'yxatda sahifadagi tovarlar uchun
    // hisoblanadi (null = hech qanday qabul yo'q).
    [property: JsonPropertyName("lastReceiptAt")] DateTime? LastReceiptAt = null,
    [property: JsonPropertyName("lastReceiptNumber")] int? LastReceiptNumber = null
);

/// <summary>
/// Seller tovar kartochkasi (detal-drawer) statistikasi: kim yetkazib beradi
/// (oxirgi kelgan postavka bo'yicha), oxirgi приход, oyiga sotilgan miqdor.
/// Narx-закупа/маржа bu yerda YO'Q (kassirga ko'rsatilmaydi).
/// </summary>
public record ProductStatsDto(
    [property: JsonPropertyName("supplierName")] string? SupplierName,
    [property: JsonPropertyName("lastReceiptAt")] DateTime? LastReceiptAt,
    [property: JsonPropertyName("lastReceiptNumber")] int? LastReceiptNumber,
    [property: JsonPropertyName("soldThisMonth")] decimal SoldThisMonth
);

/// <summary>
/// Mahsulot rasmini JSON orqali yuklash tanasi: "data:image/...;base64,..."
/// yoki to'g'ridan-to'g'ri base64 satr. (Multipart yuborishda ishlatilmaydi.)
/// </summary>
public record SetProductImageRequest(
    [property: JsonPropertyName("image")] string? Image
);

// .NET 9 records: validation attributes attached via `[property:]` only land
// on the generated property — but ASP.NET Core's model binder validates the
// PARAMETER (the constructor argument). That mismatch throws
// "validation metadata defined on property X that will be ignored" at runtime.
// Use `[param:]` for validators so they bind to the parameter; keep
// `[property:]` for serialization-only attributes (JsonPropertyName) since
// the JSON reflection target is the property.
public record CreateProductDto(
    [property: JsonPropertyName("name")]
    [param: Required(ErrorMessage = "Mahsulot nomi majburiy")]
    [param: StringLength(200, MinimumLength = 1)]
    string Name,

    [property: JsonPropertyName("isTemporary")] bool IsTemporary,

    [property: JsonPropertyName("salePrice")]
    [param: Range(0, double.MaxValue)]
    decimal SalePrice,

    [property: JsonPropertyName("minSalePrice")]
    [param: Range(0, double.MaxValue)]
    decimal MinSalePrice,

    [property: JsonPropertyName("minThreshold")]
    [param: Range(0, double.MaxValue)]
    decimal MinThreshold,

    [property: JsonPropertyName("categoryId")] int? CategoryId,
    [property: JsonPropertyName("unit")] int Unit = 1,

    // Boshlang'ich qoldiq — zakup orqali kelmagan, lekin do'konda allaqachon
    // bor tovarlar uchun. 0 bo'lsa, qoldiq keyin zakup orqali to'ldiriladi.
    [property: JsonPropertyName("quantity")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Miqdor manfiy bo'lishi mumkin emas")]
    decimal Quantity = 0,

    [property: JsonPropertyName("hidePriceFromSellers")] bool HidePriceFromSellers = false,

    // Kelgan (tannarx) narxi. Formadan ixtiyoriy kiritiladi — 0 bo'lsa keyin
    // zakup orqali to'ldiriladi. Faqat cost-ko'ruvchi (Owner/Admin) kiritadi.
    [property: JsonPropertyName("costPrice")]
    [param: Range(0, double.MaxValue)]
    decimal CostPrice = 0,

    // Artikul / SKU (ixtiyoriy).
    [property: JsonPropertyName("sku")]
    [param: StringLength(50)]
    string? Sku = null,

    [property: JsonPropertyName("description")]
    [param: StringLength(1000)]
    string? Description = null,

    [property: JsonPropertyName("isHidden")] bool IsHidden = false
);

public record UpdateProductDto(
    [property: JsonPropertyName("id")] Guid Id,

    [property: JsonPropertyName("name")]
    [param: Required(ErrorMessage = "Mahsulot nomi majburiy")]
    [param: StringLength(200, MinimumLength = 1)]
    string Name,

    [property: JsonPropertyName("salePrice")]
    [param: Range(0, double.MaxValue)]
    decimal SalePrice,

    [property: JsonPropertyName("minSalePrice")]
    [param: Range(0, double.MaxValue)]
    decimal MinSalePrice,

    [property: JsonPropertyName("minThreshold")]
    [param: Range(0, double.MaxValue)]
    decimal MinThreshold,

    [property: JsonPropertyName("categoryId")] int? CategoryId,
    [property: JsonPropertyName("unit")] int Unit = 1,
    [property: JsonPropertyName("isTemporary")] bool IsTemporary = false,
    [property: JsonPropertyName("hidePriceFromSellers")] bool HidePriceFromSellers = false,

    // Owner-only manual stock correction. Null (the default) means "leave stock
    // untouched" — the normal path for name/price edits, and for every non-Owner
    // caller, whose value the server ignores regardless. When an Owner supplies a
    // value, Quantity is set to it as an absolute figure (physical-count fix).
    // Stock otherwise moves only through zakup and sales.
    [property: JsonPropertyName("quantity")]
    [param: Range(0, double.MaxValue, ErrorMessage = "Miqdor manfiy bo'lishi mumkin emas")]
    decimal? Quantity = null,

    // Kelgan (tannarx) narxi. Null (default) — tegilmaydi. Faqat cost-ko'ruvchi
    // (Owner/Admin) yuboradi; cost-yashirin foydalanuvchida maskalangan 0 eski
    // narxni bosib ketmasligi uchun null qoldiriladi.
    [property: JsonPropertyName("costPrice")]
    [param: Range(0, double.MaxValue)]
    decimal? CostPrice = null,

    // Artikul / SKU (ixtiyoriy). Null — tegilmaydi; bo'sh satr — tozalash.
    [property: JsonPropertyName("sku")]
    [param: StringLength(50)]
    string? Sku = null,

    // Tavsif (Товары "Описание"). Edit-forma boshqaradi; null/bo'sh — tozalash.
    // POS-visibility (IsHidden) esa alohida PATCH /Products/{id} orqali.
    [property: JsonPropertyName("description")]
    [param: StringLength(1000)]
    string? Description = null
);

/// <summary>
/// Товары/Склад ekranidagi INLINE tahrirlar uchun qisman patch — har maydon
/// ixtiyoriy, faqat berilgani o'zgaradi. To'liq forma o'rniga bitta hujayra
/// (narx / min. qoldiq / ko'rinish) tez o'zgartiriladi va alohida auditlanadi.
/// </summary>
/// <summary>Bitta ombor harakati — Склад "Движение товара" oynasi uchun.</summary>
public record StockMovementDto(
    [property: JsonPropertyName("id")] Guid Id,
    // "InitialStock" | "Purchase" | "Sale" | "SaleReversal" | "Correction"
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("delta")] decimal Delta,
    [property: JsonPropertyName("resultingQty")] decimal ResultingQty,
    // Manba hujjat raqami (Ч-#### / З-###) yoki null.
    [property: JsonPropertyName("refNumber")] int? RefNumber,
    [property: JsonPropertyName("userName")] string? UserName,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

public record ProductPatchDto(
    // Sotuv narxi (inline "Цена продажи"). Null — tegilmaydi.
    [property: JsonPropertyName("salePrice")]
    [param: Range(0, double.MaxValue)]
    decimal? SalePrice = null,

    // Min. qoldiq (inline "Мин. остаток"). Null — tegilmaydi.
    [property: JsonPropertyName("minThreshold")]
    [param: Range(0, double.MaxValue)]
    decimal? MinThreshold = null,

    // POS/katalog ko'rinishi ("Скрыть/Показать"). Null — tegilmaydi.
    [property: JsonPropertyName("isHidden")] bool? IsHidden = null,

    // Ombor joyi ("Место на складе"). Null — tegilmaydi; bo'sh satr — tozalaydi.
    [property: JsonPropertyName("warehouseLocation")]
    [param: StringLength(120)]
    string? WarehouseLocation = null
);
