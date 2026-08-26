using Buildix.Application.Services;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// «Требует внимания» paneli.
///
/// <para>Bu yerdagi tekshiruvlar bitta narsani qo'riqlaydi: panelda turgan
/// ogohlantirish HOZIRGI holatga mos bo'lishi kerak. Ilgari u faqat
/// qo'shilardi — omborchi qoldiqni to'ldirgandan keyin ham «kam qoldi»
/// yozuvi turaverardi va egasi ekranda sakkiz tonna sementni ko'rib turib,
/// yonida «kam» degan xabarni o'qirdi.</para>
/// </summary>
public class NotificationReconcileTests
{
    private static NotificationService NewService(TestHarness h) =>
        new(h.Db, h.Market, h.Clock);

    private static async Task<Product> NewProductAsync(
        TestHarness h, decimal quantity, decimal minThreshold, string name = "sement")
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            MarketId = 1,
            Quantity = quantity,
            MinThreshold = minThreshold,
            SalePrice = 1200,
            CostPrice = 1000,
            MinSalePrice = 1100,
        };
        h.Db.Products.Add(product);
        await h.Db.SaveChangesAsync();
        return product;
    }

    private static Task<List<string>> KeysAsync(TestHarness h) =>
        h.Db.Notifications.Where(n => n.DedupKey != null).Select(n => n.DedupKey!).ToListAsync();

    [Fact]
    public async Task Qoldiq_kam_bolsa_ogohlantirish_paydo_boladi()
    {
        using var h = new TestHarness();
        var product = await NewProductAsync(h, quantity: 20, minThreshold: 500);

        await NewService(h).GetFeedAsync(null);

        Assert.Contains($"lowstock:{product.Id}", await KeysAsync(h));
    }

    /// <summary>
    /// Asosiy tuzatish: inventarizatsiyadan keyin ogohlantirish YO'QOLISHI
    /// kerak. Aks holda panel yolg'on gapiradi.
    /// </summary>
    [Fact]
    public async Task Qoldiq_toldirilgach_ogohlantirish_yoqoladi()
    {
        using var h = new TestHarness();
        var product = await NewProductAsync(h, quantity: 20, minThreshold: 500);
        var service = NewService(h);
        await service.GetFeedAsync(null);
        Assert.Contains($"lowstock:{product.Id}", await KeysAsync(h));

        // Omborchi inventarizatsiya qildi — qoldiq eng kam chegaradan oshdi.
        product.Quantity = 7978;
        await h.Db.SaveChangesAsync();

        await service.GetFeedAsync(null);

        Assert.DoesNotContain($"lowstock:{product.Id}", await KeysAsync(h));
    }

    /// <summary>
    /// Tovar tugab qolib, keyin ozgina to'ldirilsa: «tugadi» o'rniga «kam
    /// qoldi» turishi kerak, ikkalasi birga emas.
    /// </summary>
    [Fact]
    public async Task Tugagandan_kam_qolganga_otganda_xabar_almashadi()
    {
        using var h = new TestHarness();
        var product = await NewProductAsync(h, quantity: 0, minThreshold: 500);
        var service = NewService(h);
        await service.GetFeedAsync(null);
        Assert.Contains($"outofstock:{product.Id}", await KeysAsync(h));

        product.Quantity = 20;
        await h.Db.SaveChangesAsync();

        await service.GetFeedAsync(null);

        var keys = await KeysAsync(h);
        Assert.DoesNotContain($"outofstock:{product.Id}", keys);
        Assert.Contains($"lowstock:{product.Id}", keys);
    }

    /// <summary>
    /// Bir tovarning holati tugagani BOSHQA tovarning haqiqiy
    /// ogohlantirishini olib ketmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Boshqa_tovarning_ogohlantirishi_saqlanadi()
    {
        using var h = new TestHarness();
        var toldirilgan = await NewProductAsync(h, quantity: 20, minThreshold: 500, name: "sement");
        var hamon_kam = await NewProductAsync(h, quantity: 3, minThreshold: 50, name: "gisht");
        var service = NewService(h);
        await service.GetFeedAsync(null);

        toldirilgan.Quantity = 7978;
        await h.Db.SaveChangesAsync();

        await service.GetFeedAsync(null);

        var keys = await KeysAsync(h);
        Assert.DoesNotContain($"lowstock:{toldirilgan.Id}", keys);
        Assert.Contains($"lowstock:{hamon_kam.Id}", keys);
    }

    /// <summary>
    /// Voqea xabarlari (savdo, smena) holat emas — ular hech qachon
    /// o'chirilmasligi kerak.
    /// </summary>
    [Fact]
    public async Task Voqea_xabarlari_ochirilmaydi()
    {
        using var h = new TestHarness();
        var service = NewService(h);
        await service.RecordAsync(1, NotificationCategory.Shift, NotificationSeverity.Info,
            "Smena", "Smena yopildi", "shifts", "shift-closed:42");

        await service.GetFeedAsync(null);

        Assert.Contains("shift-closed:42", await KeysAsync(h));
    }

    /// <summary>
    /// Yuzdan ortiq tovar kam qolganda ham hech biri yo'qolmasligi kerak.
    /// Ilgari ro'yxat yuztaga kesilardi; o'sha kesim «qaysi alert hali
    /// o'rinli» degan savolga javob berish uchun ishlatilsa, yuzinchidan
    /// keyingi tovarning HAQIQIY ogohlantirishi o'chib ketardi.
    /// </summary>
    [Fact]
    public async Task Yuzdan_ortiq_kam_tovar_bolsa_hech_biri_yoqolmaydi()
    {
        using var h = new TestHarness();
        for (var i = 0; i < 130; i++)
            await NewProductAsync(h, quantity: 1, minThreshold: 50, name: $"tovar-{i}");
        var service = NewService(h);

        await service.GetFeedAsync(null);
        var afterFirst = (await KeysAsync(h)).Count;
        await service.GetFeedAsync(null);
        var afterSecond = (await KeysAsync(h)).Count;

        Assert.Equal(130, afterFirst);
        Assert.Equal(130, afterSecond);
    }
}
