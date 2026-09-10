using Buildix.Domain.Common;

namespace Buildix.Domain.Entities;

public class Zakup : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// Tovar nomi — PRIYOMKA paytidagi holicha.
    /// </summary>
    /// <remarks>
    /// <para>Priyomka hujjatida yetkazib beruvchining qarzi va to'lovi
    /// turadi, ya'ni u moliyaviy hujjat va o'zgarmasligi kerak. Nom esa
    /// jonli <see cref="Product"/> dan o'qilardi: tovar o'chirilgach
    /// hujjatda «Unknown» qolar, tovar qayta nomlanganda esa eski hujjat
    /// bugungi nomni ko'rsatardi — ikkalasi ham hisob-kitobni tekshirishni
    /// imkonsiz qilardi.</para>
    ///
    /// <para>Sotuv qatoridagi <see cref="SaleItem.ProductName"/> bilan bir
    /// xil yondashuv.</para>
    /// </remarks>
    public string ProductName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal CostPrice { get; set; }
    public Guid CreatedByAdminId { get; set; }

    // Goods-receipt grouping. A Zakup is one product line of a ZakupReceipt
    // (priyomka). Nullable for legacy rows created before the receipt model;
    // the AddSupplierAndZakupReceipt migration back-fills a 1-line receipt for
    // each, and every new line always carries a ReceiptId.
    public Guid? ReceiptId { get; set; }
    public ZakupReceipt? Receipt { get; set; }

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Navigation properties
    public Product Product { get; set; } = null!;
    public User CreatedByAdmin { get; set; } = null!;
}
