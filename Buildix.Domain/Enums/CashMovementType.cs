namespace Buildix.Domain.Enums;

/// <summary>
/// Kassa (naqd) harakati turi — Касса ledger'idagi "ТИП" ustuni. Persisted as
/// <c>integer</c>; raqamlar DB kontrakti — qayta tartiblamang, oxiriga qo'shing.
/// </summary>
public enum CashMovementType
{
    /// <summary>Smena ochilishidagi boshlang'ich qoldiq — "Открытие".</summary>
    Opening = 0,

    /// <summary>Sotuvning naqd ulushi (kirim) — "Продажа".</summary>
    Sale = 1,

    /// <summary>Qarz to'lovining naqd ulushi (kirim) — "Оплата долга".</summary>
    DebtPayment = 2,

    /// <summary>Qo'lda naqd kiritish (masalan maydalash uchun) — "Внесение".</summary>
    Deposit = 3,

    /// <summary>Xarajat (chiqim) — "Расход" (kategoriyali).</summary>
    Expense = 4,

    /// <summary>Bankka topshirish (chiqim) — "Инкассация".</summary>
    Collection = 5,
}
