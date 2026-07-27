using Buildix.Domain.Constants;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// S0 — seller ruxsat modeli. Yangi risk-amallar (возврат, приёмка) alohida
/// kalitlar bilan gate qilinadi: seller default'ida YO'Q (admin yoqadi), Admin'da
/// BOR, katalogda ro'yxatga olingan. «Off → panelda ko'rinmaydi» tamoyilining
/// backend tomoni: kalit yo'q → endpoint 403, UI banni yashiradi.
/// </summary>
public class SellerPermissionTests
{
    [Fact]
    public void New_risk_keys_are_registered_in_the_catalogue()
    {
        Assert.Contains(PermissionKeys.SalesReturn, PermissionKeys.All);
        Assert.Contains(PermissionKeys.ZakupAccept, PermissionKeys.All);
        Assert.True(PermissionKeys.IsValid("sales.return"));
        Assert.True(PermissionKeys.IsValid("zakup.accept"));
    }

    [Fact]
    public void Seller_default_does_NOT_include_return_or_accept()
    {
        var seller = PermissionDefaults.ForRole(Role.Seller);
        // Kassir default holatda возврат/приёмка qila olmaydi — Owner alohida yoqadi.
        Assert.DoesNotContain(PermissionKeys.SalesReturn, seller);
        Assert.DoesNotContain(PermissionKeys.ZakupAccept, seller);
        // «Все продажи» ham default emas — kassir faqat o'z sotuvlarini ko'radi
        // (server-side majburlanadi); do'kon bo'ylab ko'rish — Owner grant'i.
        Assert.DoesNotContain(PermissionKeys.DataAllSalesView, seller);
        // Lekin ko'rish/sotuv kabi asosiy kalitlar bor.
        Assert.Contains(PermissionKeys.SalesAccess, seller);
        Assert.Contains(PermissionKeys.ZakupAccess, seller);
    }

    [Fact]
    public void Admin_default_includes_return_and_accept()
    {
        var admin = PermissionDefaults.ForRole(Role.Admin);
        // Admin = All minus sezgir ko'rinishlar — yangi kalitlar avtomatik kiradi,
        // shuning uchun endpointlarni re-gate qilish admin ishini buzmaydi.
        Assert.Contains(PermissionKeys.SalesReturn, admin);
        Assert.Contains(PermissionKeys.ZakupAccept, admin);
    }
}
