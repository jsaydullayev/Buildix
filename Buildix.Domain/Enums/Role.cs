namespace Buildix.Domain.Enums;

/// <summary>
/// User role. Persisted as int and security-critical — explicit values are the
/// DB contract; a silent renumber would re-map every user's privileges.
/// </summary>
public enum Role
{
    SuperAdmin = 0,  // Barcha marketlarni boshqaradi
    Owner = 1,       // Faqat o'z marketini boshqaradi
    Admin = 2,       // Faqat o'z marketida ma'lum huquqlar
    Seller = 3       // Faqat o'z marketida sotuv
}
