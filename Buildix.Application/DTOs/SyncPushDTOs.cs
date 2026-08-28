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
    /// <summary>
    /// Do'konda yaratilgan xodimlar.
    /// </summary>
    /// <remarks>
    /// <para>Ular ENG BIRINCHI yuboriladi, chunki har sotuv o'z sotuvchisiga
    /// ishora qiladi (<c>Sale.SellerId</c>). Ilgari xodimlar umuman
    /// yuborilmasdi va do'konda yangi kassir qo'shilishi butun
    /// sinxronizatsiyani o'ldirardi: uning birinchi cheki bulutda tashqi
    /// kalitni buzar, tranzaksiya orqaga qaytar, keyingi daqiqada AYNAN
    /// o'sha paket qayta yuborilar va yana yiqilardi — abadiy.</para>
    /// </remarks>
    public List<User> Users { get; set; } = new();

    public List<Product> Products { get; set; } = new();
    public List<Customer> Customers { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<Sale> Sales { get; set; } = new();
    public List<SaleItem> SaleItems { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    /// <summary>
    /// Qarzlar — sotuvdan KEYIN, chunki har qarz o'z chekiga bog'langan.
    /// </summary>
    /// <remarks>
    /// <para>Ilgari qarzlar umuman yuborilmasdi. Do'konda o'nlab mijozning
    /// millionlab so'm qarzi bo'lsa ham, egasining telefonidagi «Qarzlar»
    /// ekrani NOL ko'rsatardi va Telegram botining qarz hisoboti ham bo'sh
    /// kelardi. Egasi qarz undirish bo'yicha qaror qabul qila olmasdi —
    /// ekran unga muammo yo'q deb ko'rsatardi.</para>
    /// </remarks>
    public List<Debt> Debts { get; set; } = new();

    /// <summary>Qaytarishlar va ularning qatorlari.</summary>
    /// <remarks>
    /// <para>Ilgari yuborilmasdi: bulutdagi sotuv summasi qaytarishni
    /// hisobga olmasdi va egasining tushumi haqiqiydan KATTA ko'rinardi.</para>
    /// </remarks>
    public List<SaleReturn> SaleReturns { get; set; } = new();
    public List<SaleReturnItem> SaleReturnItems { get; set; } = new();

    /// <summary>Yetkazib beruvchilar, qabullar va xaridlar.</summary>
    /// <remarks>
    /// <para>Ilgari yuborilmasdi: bulutdagi kunlik hisobotda xarid summasi
    /// doim NOL bo'lardi, ya'ni sof foyda ham noto'g'ri chiqardi.</para>
    /// </remarks>
    public List<Supplier> Suppliers { get; set; } = new();
    public List<ZakupReceipt> ZakupReceipts { get; set; } = new();
    public List<Zakup> Zakups { get; set; } = new();

    /// <summary>Kassa harakatlari va ombor jurnali.</summary>
    /// <remarks>
    /// <para>Ilgari yuborilmasdi: bulutda kassa qoldig'i va tovar harakati
    /// tarixi umuman ko'rinmasdi.</para>
    /// </remarks>
    public List<CashMovement> CashMovements { get; set; } = new();
    public List<StockMovement> StockMovements { get; set; } = new();

    public bool IsEmpty => TotalRows == 0;

    public int TotalRows =>
        Users.Count + Suppliers.Count + Products.Count + Customers.Count + Shifts.Count
        + Sales.Count + SaleItems.Count + Payments.Count
        + Debts.Count + SaleReturns.Count + SaleReturnItems.Count
        + ZakupReceipts.Count + Zakups.Count
        + CashMovements.Count + StockMovements.Count;
}

/// <summary>
/// Bulutning javobi.
///
/// <para><b><see cref="Deferred"/> nima uchun alohida.</b> Bola qator otasi
/// hali yuborilmagan bo'lishi mumkin: sotuvlar paketi to'lgan va o'sha
/// sotuv keyingi safarga qolgan. Bunday qatorni rad etib, do'kon belgisini
/// oldinga surish uni ABADIY yo'qotardi — sotuv keyin yetib borar, qatori
/// esa hech qachon. Shuning uchun u «kechiktirildi» deb qaytariladi va
/// do'kon o'sha jadval belgisini surmaydi: keyingi urinishda otasi yetib
/// borgach, qator ham o'tadi.</para>
///
/// <para>Bu BEGONA otadan farq qiladi: begona sotuvga tegishli qator
/// butunlay rad etiladi va jurnalga yoziladi.</para>
/// </summary>
public record SyncPushResultDto(
    int Accepted,
    IReadOnlyDictionary<string, int> PerTable,
    IReadOnlyDictionary<string, int> Deferred);
