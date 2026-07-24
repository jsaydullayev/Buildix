using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Kassa (naqd) harakati jurnali — Касса ekranidagi tiplangan operatsiyalar
/// ro'yxati (ВРЕМЯ/ТИП/ОПИСАНИЕ/КТО/СУММА). Append-only.
///
/// MUHIM: bu ledger naqd BALANSNI belgilamaydi — balans avtoritativ
/// <see cref="CashRegister.CurrentBalance"/> da turadi (sotuv/qarz/chiqim uni
/// bevosita o'zgartiradi). CashMovement o'sha hodisalarni RO'YXAT/AUDIT sifatida
/// aks ettiradi: agar biror hook o'tkazib yuborilsa, faqat ro'yxat to'liqsiz
/// bo'ladi, balans emas. Har yozuv o'z hodisasining tranzaksiyasi ichida yoziladi.
/// </summary>
public class CashMovement : BaseEntity
{
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    /// <summary>Qaysi smenaga tegishli (ixtiyoriy — smenadan tashqari ham bo'lishi mumkin).</summary>
    public Guid? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public CashMovementType Type { get; set; }

    /// <summary>Naqd o'zgarishi (± ). Kirim musbat, chiqim manfiy.</summary>
    public decimal Amount { get; set; }

    /// <summary>Xarajat kategoriyasi (Расход uchun): Хозяйственные/Доставка/Аванс/Прочее. Aks holda null.</summary>
    public string? Category { get; set; }

    /// <summary>Manba hujjat raqami: Sale/DebtPayment → Sale.SaleNumber (Ч-####). Aks holda null.</summary>
    public int? RefNumber { get; set; }

    public string? Comment { get; set; }

    /// <summary>Kim (JWT actor). Tizim hodisasi uchun null bo'lishi mumkin.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }
}
