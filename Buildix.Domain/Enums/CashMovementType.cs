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

    /// <summary>
    /// Katalogda yo'q tovar uchun qo'shni do'konga to'langan pul (chiqim) —
    /// "Чужой товар".
    ///
    /// <para>Mijoz so'ragan narsa bizda bo'lmasa, u qo'shni do'kondan olinadi
    /// va puli kassadan beriladi. Bu chiqim ilgari hech qayerda qayd
    /// etilmasdi: mijozdan olingan pul to'liq kassada ko'rinar, qo'shniga
    /// berilgani esa yo'q — natijada smena yakunida naqd har doim kamayib
    /// chiqardi.</para>
    /// </summary>
    ExternalPurchase = 6,
}
