using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>Obuna to'lovi qanday qabul qilingani.</summary>
public enum PaymentChannel
{
    Cash = 0,
    Click = 1,
    Payme = 2,
    /// <summary>Bank o'tkazmasi / hisob raqami.</summary>
    Transfer = 3
}

/// <summary>
/// Qabul qilingan obuna to'lovi — «Оплата получена» tugmasining izi.
///
/// <para><b>Qaytarib bo'lmaydi.</b> Qator o'chirilmaydi va tahrirlanmaydi;
/// xato bo'lsa teskari yozuv qo'shiladi. Shu sababli tugma bosilishidan oldin
/// modal natijani (yangi «Оплачен до» sanasini) ko'rsatadi.</para>
///
/// <para><b>Summa va tarif shu yerda qotadi.</b> Narx keyin o'zgarsa
/// (<see cref="PlatformPlan"/>), o'tgan to'lovlar tarixi o'zgarmaydi.</para>
/// </summary>
public class SubscriptionPayment : BaseEntity
{
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    /// <summary>To'lov paytidagi tarif.</summary>
    public PlanCode Plan { get; set; }

    /// <summary>To'langan summa (so'm) — tarif narxi × oylar soni.</summary>
    public decimal AmountUzs { get; set; }

    /// <summary>Nechta oyga to'landi.</summary>
    public int Months { get; set; }

    public PaymentChannel Channel { get; set; }

    public DateTime PaidAtUtc { get; set; }

    /// <summary>To'lovdan KEYINGI obuna tugash sanasi — hisob-kitobni qayta tiklash uchun.</summary>
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>Qaysi SuperAdmin qabul qildi.</summary>
    public Guid AcceptedByUserId { get; set; }

    public string? Note { get; set; }
}
