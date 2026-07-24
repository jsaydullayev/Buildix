namespace Buildix.Domain.Enums;

/// <summary>
/// Bildirishnoma kategoriyasi — Уведомления ekranidagi tablar (Склад/Долги/
/// Смены/Поставки). Persisted as <c>integer</c>.
/// </summary>
public enum NotificationCategory
{
    Warehouse = 0, // Склад — kam/tugagan qoldiq
    Debt = 1,      // Долги — qarz muddati/просрочен
    Shift = 2,     // Смены — smena yopildi, kassa farqi, naqd yechish so'rovi
    Supply = 3,    // Поставки — xarid qabul qilindi / yo'lda
}

/// <summary>Bildirishnoma darajasi — feed'dagi rang.</summary>
public enum NotificationSeverity
{
    Info = 0,     // ko'k
    Success = 1,  // yashil
    Warning = 2,  // sariq
    Danger = 3,   // qizil
}
