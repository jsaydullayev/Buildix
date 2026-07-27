using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Tarif narxi va limitlari — platforma bo'yicha bitta jadval, uch qator.
///
/// <para><b>Nega alohida jadval, konstanta emas.</b> Narxni operator
/// «Настройки → Тарифы» dan o'zgartiradi; kodga yozilsa, har narx o'zgarishi
/// deploy talab qilardi.</para>
///
/// <para><b>Nega to'lov o'z summasini saqlaydi.</b> Bu yerdagi narx —
/// KELAJAK to'lovlar uchun. Allaqachon qabul qilingan
/// <see cref="SubscriptionPayment"/> o'z summasini yozib qo'yadi, shuning
/// uchun narx ko'tarilsa tarix qayta yozilmaydi.</para>
/// </summary>
public class PlatformPlan
{
    /// <summary>Birlamchi kalit — tarif kodining o'zi (uch qatordan ortiq bo'lmaydi).</summary>
    public PlanCode Code { get; set; }

    /// <summary>Oylik narx (so'm).</summary>
    public decimal PriceUzs { get; set; }

    /// <summary>Foydalanuvchilar chegarasi. <c>0</c> = limitsiz (Pro).</summary>
    public int MaxUsers { get; set; }

    /// <summary>Savdo nuqtalari chegarasi.</summary>
    public int MaxPoints { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
