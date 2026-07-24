using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class Product : BaseEntity, ISoftDelete
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Artikul / SKU — do'kon ichidagi mahsulot kodi (masalan "CEM-500").
    /// Ixtiyoriy. Qidiruvda ishlatiladi; market ichida takrorlanishi mumkin
    /// (unikal cheklov yo'q — ba'zi do'konlar bir kodni variantlarga beradi).
    /// </summary>
    public string? Sku { get; set; }

    /// <summary>
    /// Mahsulot rasmiga server-nisbiy URL, masalan "/uploads/products/12/abc.webp".
    /// Null = rasmsiz (ko'pchilik tovarlar uchun odatiy holat). Rasm fayli diskda
    /// (persistent volume) saqlanadi; bu yerda faqat qisqa yo'l turadi.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Sotuvchiga ko'rinadigan qisqa tavsif (Товары ekranidagi "Описание",
    /// kassir tovar kartochkasida ko'radi). Ixtiyoriy.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Omborда saqlash joyi ("Место на складе" — masalan «Навес А · ряд 2»).
    /// Sotuvchi tovar kartochkasida ko'radi; qidirib topishga yordam beradi. Ixtiyoriy.
    /// </summary>
    public string? WarehouseLocation { get; set; }

    /// <summary>
    /// True bo'lsa — mahsulot POS (kassa) va sotuvchi katalogidan yashiriladi,
    /// lekin hisobotlar/tarixda qoladi. Bu <see cref="HidePriceFromSellers"/> dan
    /// FARQLI: u faqat NARXNI yashiradi, bu esa TOVARNI butunlay katalogdan
    /// olib qo'yadi (masalan, vaqtincha sotuvdan chiqarilgan tovar).
    /// </summary>
    public bool IsHidden { get; set; } = false;

    public bool IsTemporary { get; set; } = false;
    public Guid? CreatedBySellerId { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // Pricing
    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal MinSalePrice { get; set; }

    /// <summary>
    /// True bo'lsa, bu mahsulotning narxi sotuv (POS) oqimida Seller roliga
    /// ko'rsatilmaydi — kassir narxni qo'lda kiritadi. Mahsulotlar bo'limida
    /// narx baribir ko'rinadi. Admin/Owner mahsulot formasidan boshqaradi.
    /// </summary>
    public bool HidePriceFromSellers { get; set; } = false;

    // Stock - DECIMAL qilib o'zgartirdik (1.5 kg bo'lishi mumkin)
    public decimal Quantity { get; set; }
    public decimal MinThreshold { get; set; } = 5m;

    // Optimistic concurrency token. Mapped to PostgreSQL's hidden xmin column
    // so concurrent stock changes detect each other and surface a 409.
    public uint Xmin { get; set; }


    public UnitType Unit { get; set; } = UnitType.Piece;

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Category
    public int? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }

    // Navigation properties
    public User? CreatedBySeller { get; set; }
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<Zakup> Zakups { get; set; } = new List<Zakup>();

    /// <summary>
    /// Omborda mavjudligini tekshirish
    /// </summary>
    public bool IsInStock(decimal requestedQuantity)
    {
        return Quantity >= requestedQuantity;
    }

    /// <summary>
    /// Minimal miqdordan pastga tushganmi
    /// </summary>
    public bool IsLowStock => Quantity <= MinThreshold;

    /// <summary>
    /// Unit nomini olish (uzbek)
    /// </summary>
    public string GetUnitName()
    {
        return Unit switch
        {
            UnitType.Piece => "dona",
            UnitType.Kilogram => "kg",
            UnitType.Meter => "m",
            UnitType.Bag => "qop",
            UnitType.Ton => "t",
            UnitType.Sheet => "list",
            UnitType.Bucket => "chelak",
            UnitType.Roll => "rulon",
            UnitType.Box => "quti",
            UnitType.Pack => "pachka",
            UnitType.Liter => "l",
            _ => "noma'lum"
        };
    }
}
