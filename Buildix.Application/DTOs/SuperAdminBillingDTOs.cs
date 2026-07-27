using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Application.DTOs;

/// <summary>Tarif kartochkasi — narx, limitlar va nechta do'kon shu tarifda.</summary>
public record SaPlanDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("priceUzs")] decimal PriceUzs,
    [property: JsonPropertyName("maxUsers")] int MaxUsers,
    [property: JsonPropertyName("maxPoints")] int MaxPoints,
    [property: JsonPropertyName("stores")] int Stores
);

/// <summary>«Подписки и оплаты» jadvalining bir qatori.</summary>
public record SaBillingRowDto(
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("priceUzs")] decimal PriceUzs,
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt,
    /// <summary>«Active» | «Soon» | «Overdue» | «Blocked».</summary>
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("lastPaymentAtUtc")] DateTime? LastPaymentAtUtc,
    [property: JsonPropertyName("lastPaymentChannel")] string? LastPaymentChannel,
    /// <summary>
    /// Egasi Telegramni bog'laganmi. <c>false</c> bo'lsa eslatma unga YETIB
    /// BORMAYDI — operator uni qo'ng'iroq bilan xabardor qilishi kerak,
    /// shuning uchun bu ro'yxatda ko'rinadi.
    /// </summary>
    [property: JsonPropertyName("ownerTelegramLinked")] bool OwnerTelegramLinked = false
);

/// <summary>
/// To'lov natijasining OLDINDAN ko'rinishi. Operator tugmani bosishdan oldin
/// aynan qaysi sanaga o'tishini ko'radi — hisob-kitob server tomonda, ya'ni
/// ko'rilgan narsa yoziladi.
/// </summary>
public record SaPaymentPreviewDto(
    [property: JsonPropertyName("currentExpiresAt")] DateTime? CurrentExpiresAt,
    [property: JsonPropertyName("newExpiresAt")] DateTime NewExpiresAt,
    [property: JsonPropertyName("amountUzs")] decimal AmountUzs,
    [property: JsonPropertyName("plan")] string Plan,
    /// <summary>
    /// <c>true</c> — langar eski muddat (xizmat uzilmagan, foydalanilgan
    /// kunlar uchun to'lanadi); <c>false</c> — langar bugun (xizmat uzilgan
    /// edi, o'chiq turgan davr uchun pul olinmaydi).
    /// </summary>
    [property: JsonPropertyName("anchoredOnExpiry")] bool AnchoredOnExpiry,
    /// <summary>
    /// <c>true</c> — muddat operator tomonidan QO'LDA kiritilgan, ya'ni
    /// langar qoidasi (eski sana / bugun) qo'llanmagan. Interfeys shunda
    /// boshqa izoh ko'rsatadi: aks holda «bugundan uzaytirildi» degan
    /// noto'g'ri tushuntirish chiqardi.
    /// </summary>
    [property: JsonPropertyName("manual")] bool Manual = false
);

/// <summary>
/// To'lovni yozish so'rovi.
///
/// <para><c>channel</c> va <c>plan</c> — MATN («Click», «Pro»). API global
/// <c>JsonStringEnumConverter</c> ni yoqmagan (raqamli enum kutadi), qolgan
/// DTO'lar ham chegarada matn ishlatadi — shu qoidadan chetga chiqilmaydi,
/// aks holda klient «1» yoki «Click» ekanini taxmin qilishga majbur bo'lardi.</para>
/// </summary>
public record SaRecordPaymentDto(
    [property: JsonPropertyName("months")]
    [param: Range(1, 24)]
    int Months,
    [property: JsonPropertyName("channel")] string Channel,
    /// <summary>Tarifni ham o'zgartirish (null/bo'sh = tegilmaydi).</summary>
    [property: JsonPropertyName("plan")] string? Plan = null,
    [property: JsonPropertyName("note")]
    [param: StringLength(300)]
    string? Note = null,
    /// <summary>
    /// Yangi tugash sanasi — QO'LDA kiritilganda. <c>null</c> bo'lsa muddat
    /// odatdagi qoida bo'yicha hisoblanadi (eski sanadan yoki bugundan
    /// <see cref="Months"/> oy). To'lov summasi baribir <see cref="Months"/>
    /// dan olinadi: operator «3 oylik pul oldim, lekin sanani 15-sentabrga
    /// qo'y» deyishi mumkin.
    /// </summary>
    [property: JsonPropertyName("expiresAt")] DateTime? ExpiresAt = null
)
{
    public SaRecordPaymentDto() : this(1, nameof(PaymentChannel.Cash)) { }

    /// <summary>Matnni enumga aylantiradi; noto'g'ri qiymat — foydalanuvchiga tushunarli xato.</summary>
    public PaymentChannel ParseChannel() =>
        Enum.TryParse<PaymentChannel>(Channel, ignoreCase: true, out var c)
            ? c
            : throw new InvalidOperationException($"To'lov usuli noma'lum: '{Channel}'.");

    public PlanCode? ParsePlan()
    {
        if (string.IsNullOrWhiteSpace(Plan)) return null;
        return Enum.TryParse<PlanCode>(Plan, ignoreCase: true, out var p)
            ? p
            : throw new InvalidOperationException($"Tarif noma'lum: '{Plan}'.");
    }
}

public record SaPaymentResultDto(
    [property: JsonPropertyName("paymentId")] Guid PaymentId,
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("amountUzs")] decimal AmountUzs,
    [property: JsonPropertyName("newExpiresAt")] DateTime NewExpiresAt
);

/// <summary>«Последние платежи» ro'yxatining qatori.</summary>
public record SaPaymentLogDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("storeName")] string StoreName,
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("amountUzs")] decimal AmountUzs,
    [property: JsonPropertyName("paidAtUtc")] DateTime PaidAtUtc
);
