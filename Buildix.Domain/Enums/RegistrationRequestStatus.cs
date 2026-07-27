namespace Buildix.Domain.Enums;

/// <summary>
/// Ulanish arizasining holati. Qiymatlar DB kontrakti — o'zgartirilmaydi,
/// faqat oxiriga qo'shiladi.
///
/// <para>Operator oqimi (dizayn: «Заявки на подключение»):
/// <c>Pending</c> → qo'ng'iroq qilinadi → <c>Accepted</c> → «Создать магазин»
/// → <c>Approved</c>. <c>Accepted</c> qadami ataylab alohida: telefonda
/// gaplashish bilan do'kon yaratish orasida odatda kunlar bo'ladi va bu
/// oraliqda ariza «yangi»lar ro'yxatida turib qolsa, operator o'sha odamga
/// ikkinchi marta qo'ng'iroq qilardi.</para>
///
/// <para>«Подключена» alohida holat EMAS — u <c>Approved</c> va
/// <c>CreatedMarketId != null</c> dan hisoblanadi.</para>
/// </summary>
public enum RegistrationRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    /// <summary>Qabul qilindi (bog'lanildi), lekin do'kon hali yaratilmagan.</summary>
    Accepted = 3
}
