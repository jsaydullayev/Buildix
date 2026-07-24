using Buildix.Domain.Enums;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Kassa harakati jurnaliga (CashMovement) yozuv qo'shadi. Yozuv chaqiruvchining
/// DbContext'iga qo'shiladi, lekin SAQLANMAYDI — chaqiruvchi o'z SaveChanges'ida
/// (o'z tranzaksiyasi ichida) saqlaydi. Shu bilan harakat naqd hodisasi bilan
/// atomik yoziladi.
///
/// Bu ledger balansni BELGILAMAYDI (balans CashRegister.CurrentBalance'da) —
/// faqat Касса ekranidagi tiplangan ro'yxat/aggregatlar uchun.
/// </summary>
public interface ICashLedger
{
    /// <param name="marketId">Market (tenant).</param>
    /// <param name="amount">± naqd o'zgarishi (kirim musbat, chiqim manfiy).</param>
    /// <param name="type">Harakat turi.</param>
    /// <param name="userId">JWT actor, ixtiyoriy.</param>
    /// <param name="shiftId">Tegishli smena, ixtiyoriy.</param>
    /// <param name="refNumber">Manba hujjat raqami (Ч-####), ixtiyoriy.</param>
    /// <param name="category">Xarajat kategoriyasi (Расход uchun), ixtiyoriy.</param>
    /// <param name="comment">Izoh, ixtiyoriy.</param>
    void Record(int marketId, decimal amount, CashMovementType type,
        Guid? userId = null, Guid? shiftId = null, int? refNumber = null,
        string? category = null, string? comment = null);
}
