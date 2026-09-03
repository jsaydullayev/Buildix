using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class Sale : BaseEntity, ISoftDelete
{
    /// <summary>
    /// Per-market ketma-ket chek raqami (dizaynda «ЧЕК №1046»). Draft yaratilganda
    /// beriladi (market bo'yicha max+1). 0 = hali berilmagan (eski yozuvlar).
    /// Guid Id tashqi ko'rsatishga yaroqsiz bo'lgani uchun qo'shildi.
    /// </summary>
    public int SaleNumber { get; set; }

    /// <summary>
    /// Chek QAYSI kassada urilgani — texnik sozlashda qo'yiladigan qisqa
    /// belgi («A», «B», «1»).
    /// </summary>
    /// <remarks>
    /// <para><b>Nega sotuvchi yetarli emas.</b> Bitta kassir kun davomida
    /// ikkala kassada ham ishlashi mumkin, ikki kassir esa bitta login
    /// ostida ishlashi mumkin. Ya'ni <c>SellerId</c> «qaysi kassada
    /// sotilgan» degan savolga javob bermaydi — u faqat «kim sotgan» ni
    /// aytadi.</para>
    ///
    /// <para><b>Nega so'rov bilan keladi, sozlamadan emas.</b> Lokal tarmoq
    /// rejimida 2-kassaning o'z API si YO'Q — uning so'rovlari server
    /// kassaning API siga boradi. Serverning sozlamasidan olinsa, har bir
    /// chek serverning belgisi bilan yozilardi. Shuning uchun belgi
    /// so'rovning o'zida keladi (<c>X-Buildix-Register</c>) va uni qobiq
    /// qo'yadi.</para>
    ///
    /// <para><c>null</c> — belgi qo'yilmagan kassa yoki brauzerdan kirilgan
    /// (egasi telefonda). Eski yozuvlarda ham <c>null</c>.</para>
    /// </remarks>
    public string? RegisterCode { get; set; }

    public Guid SellerId { get; set; }

    /// <summary>
    /// The cash shift this sale was rung up in — stamped from the seller's open
    /// shift at creation. Lets a receipt print "Смена №N" and ties a sale to the
    /// drawer session it belongs to instead of inferring it from timestamps.
    /// Null for sales made with no shift open (Admin/Owner) and for rows created
    /// before this column existed.
    /// </summary>
    public Guid? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public Guid? CustomerId { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }

    // Sale-level discount (skidka), in currency. Subtracted from the gross item
    // sum when computing the charged TotalAmount — see
    // SaleService.RecalculateSaleTotalAsync. Item SalePrices are left untouched,
    // so per-item history and the invoice line items stay intact; only the bill
    // total drops. 0 = no discount.
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Bu yozuv SAVDO emas — mijozning tizimdan OLDINGI qarzini olib yuruvchi
    /// texnik qator.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega bunday yozuv bor.</b> Qarz har doim savdoga bog'lanadi
    /// (<c>Debt.SaleId</c>). Do'kon Buildix'ga o'tayotganda mijozlarning eski
    /// qarzi ham kiritiladi, lekin unga mos savdo tizimda yo'q — shu sababli
    /// tovarsiz qator yaratiladi.</para>
    ///
    /// <para><b>Nega belgi kerak.</b> Belgisiz u hisobotlarga oddiy savdo
    /// bo'lib kirardi: kiritilgan kuni tushum bir million so'mga ko'tarilardi,
    /// chek soniga qo'shilardi, o'rtacha chekni buzardi va tovari yo'qligi
    /// uchun marjani yerga urardi. Egasi hisobotda «bu pul qayerdan keldi?»
    /// degan savolga javob topa olmasdi — chunki uning ortida hech qanday
    /// tovar yo'q edi.</para>
    ///
    /// <para>Qarz moduli bu qatorni ODATDAGIDEK ko'radi: to'lov ham shu yerga
    /// yoziladi. Faqat TUSHUM hisobi uni chetlab o'tadi. To'langan pul
    /// kassaga kirim bo'lib tushadi — u yerda u haqiqatan ham pul
    /// harakati.</para>
    /// </remarks>
    public bool IsOpeningBalance { get; set; } = false;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // Optimistic concurrency token. Mapped to PostgreSQL's built-in xmin column
    // so concurrent payment / cancellation writes detect each other instead of
    // silently overwriting Status (Paid vs Debt vs Cancelled).
    public uint Xmin { get; set; }

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Navigation properties
    public User Seller { get; set; } = null!;
    public Customer? Customer { get; set; }
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public Debt? Debt { get; set; }
}
