namespace Buildix.Domain.Enums;

/// <summary>
/// Obuna tarifi. Qiymatlar DB kontrakti — o'zgartirilmaydi, faqat oxiriga
/// qo'shiladi. Narx va limitlar bu yerda EMAS
/// (<see cref="Entities.PlatformPlan"/> da): ular operator tomonidan
/// o'zgartiriladi, kod esa o'zgarmaydi.
/// </summary>
public enum PlanCode
{
    /// <summary>1 nuqta · 3 foydalanuvchigacha · kassa, ombor, qarzlar.</summary>
    Start = 0,
    /// <summary>1 nuqta · 8 foydalanuvchigacha · + hisobotlar, qaytarish, smenalar.</summary>
    Standard = 1,
    /// <summary>3 nuqtagacha · foydalanuvchi limitisiz · + API va eksport.</summary>
    Pro = 2
}
