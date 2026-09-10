using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class SaleItem : BaseEntity
{
    public Guid SaleId { get; set; }


    public bool IsExternal { get; set; } = false;


    public Guid? ProductId { get; set; }


    public string? ExternalProductName { get; set; }
    public decimal ExternalCostPrice { get; set; }

    /// <summary>
    /// Tovar nomi — SOTUV paytidagi holicha yozib qo'yiladi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega nusxa.</b> Ilgari chek, savdo tarixi, qarzlar va
    /// hisobotlar nomni jonli <see cref="Product"/> jadvalidan olardi.
    /// Natijada tovar o'chirilishi bilan ESKI cheklarda ham nom yo'qolib,
    /// «Noma'lum mahsulot» bo'lib qolardi — ya'ni allaqachon bosilib
    /// berilgan hujjat keyinchalik o'zgarib ketardi. Mahsulot nomi
    /// tahrirlanganda ham xuddi shu edi: bir yil oldingi chek bugungi
    /// nomni ko'rsatardi.</para>
    ///
    /// <para>Endi hujjat o'zining nusxasini saqlaydi va tovarga bog'liq
    /// emas. Bu <see cref="SaleReturnItem.ProductName"/> da allaqachon
    /// qo'llangan yondashuvning o'zi.</para>
    ///
    /// <para>Tashqi (katalogda yo'q) tovarda bu maydon
    /// <see cref="ExternalProductName"/> bilan bir xil to'ldiriladi —
    /// o'qiydigan tomon ikkita maydonni farqlab o'tirmasin.</para>
    /// </remarks>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// O'lchov birligi — sotuv paytidagi holicha ("qop", "m", "kg").
    /// Nomi bilan bir sababdan nusxa olinadi: busiz o'chirilgan tovarning
    /// qatori «2 qop» o'rniga shunchaki «2» bo'lib qolardi.
    /// </summary>
    public UnitType ProductUnit { get; set; } = UnitType.Piece;

    /// <summary>
    /// Ekranga/qog'ozga chiqadigan nom. Tashqi tovarda uning nomi, aks
    /// holda sotuv paytidagi nusxa; ikkalasi ham bo'sh bo'lsa (faqat eski,
    /// to'ldirilmagan yozuvlarda) jonli tovar nomiga qaytiladi.
    /// </summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(ExternalProductName) ? ExternalProductName!
        : !string.IsNullOrWhiteSpace(ProductName) ? ProductName
        : Product?.Name ?? string.Empty;

    // Quantity - DECIMAL qilib o'zgartirdik
    public decimal Quantity { get; set; }

    public decimal CostPrice { get; set; }
    public decimal SalePrice { get; set; }
    public string? Comment { get; set; }

    // Navigation properties
    public Sale Sale { get; set; } = null!;


    public Product? Product { get; set; }

    /// <summary>
    /// Effective cost price calculation (External for external products, otherwise Product.CostPrice)
    /// </summary>
    public decimal EffectiveCostPrice => IsExternal
        ? ExternalCostPrice
        : (Product?.CostPrice ?? 0);

    /// <summary>
    /// Jami summa (Quantity * SalePrice)
    /// </summary>
    public decimal TotalPrice => Quantity * SalePrice;

    /// <summary>
    /// Foyda (SalePrice - EffectiveCostPrice) * Quantity
    /// </summary>
    public decimal Profit => (SalePrice - EffectiveCostPrice) * Quantity;

    /// <summary>
    /// Jami xaraj narx (Quantity * EffectiveCostPrice)
    /// </summary>
    public decimal TotalCost => Quantity * EffectiveCostPrice;
}
