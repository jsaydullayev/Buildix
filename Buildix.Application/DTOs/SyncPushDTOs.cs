using Buildix.Domain.Entities;

namespace Buildix.Application.DTOs;

/// <summary>
/// Do'kondan bulutga yuboriladigan to'plam.
///
/// <para><b>Tartib MUHIM va u shu yerda belgilanadi.</b> Bulut ro'yxatlarni
/// aynan shu ketma-ketlikda yozadi: tovar va mijoz sotuvdan OLDIN, sotuv esa
/// o'z qatorlari va to'lovlaridan oldin. Aks holda tashqi kalit buzilar va
/// butun to'plam rad etilardi — do'kon esa har safar o'sha ma'lumotni qayta
/// yuborib, hech qachon o'ta olmasdi.</para>
///
/// <para>Yozuvlar entity ko'rinishida yuboriladi (sabab:
/// <c>EntityWireFormat</c>).</para>
/// </summary>
public class SyncPushDto
{
    public List<Product> Products { get; set; } = new();
    public List<Customer> Customers { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Sale> Sales { get; set; } = new();
    public List<SaleItem> SaleItems { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    public bool IsEmpty =>
        Products.Count == 0 && Customers.Count == 0 && Shifts.Count == 0
        && Sales.Count == 0 && SaleItems.Count == 0 && Payments.Count == 0;

    public int TotalRows =>
        Products.Count + Customers.Count + Shifts.Count
        + Sales.Count + SaleItems.Count + Payments.Count;
}

/// <summary>Bulutning javobi: nechta qator qabul qilindi.</summary>
public record SyncPushResultDto(
    int Accepted,
    IReadOnlyDictionary<string, int> PerTable);
