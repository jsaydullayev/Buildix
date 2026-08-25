using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Katalogda yo'q tovar uchun qo'shni do'konga to'langan naqd.
///
/// <para><b>Muammo.</b> Bunday tovar bizniki emas — uni qo'shni do'kondan olib,
/// puli kassadan beriladi. Ilgari bu chiqim hech qayerda qayd etilmasdi:
/// mijozdan olingan pul to'liq kassada ko'rinar, qo'shniga berilgani esa yo'q.
/// Foyda to'g'ri hisoblanardi (ExternalCostPrice ayiriladi), lekin kassadagi
/// naqd har smenada kamayib chiqardi va sverka farq berardi.</para>
///
/// <para><b>Qachon yoziladi.</b> Sotuv Draft holatidan CHIQQAN paytda — ya'ni
/// chek yakunlanganda. Qoralama tashlab yuborilishi mumkin, shuning uchun tovar
/// qo'shilgan zahoti emas. Qarzga sotilganda ham yoziladi: mijoz keyin
/// to'laydi, qo'shniga esa pul allaqachon berilgan.</para>
///
/// <para><b>Aynan bir marta.</b> Chaqiruvchi statusni o'zgartirishdan OLDIN
/// «Draft edimi» ni eslab qoladi va faqat o'sha holatda chaqiradi. Sotuv
/// Draft'dan bir marta chiqadi (bekor qilish — terminal holat), shuning uchun
/// takror yozilmaydi.</para>
/// </summary>
public interface IExternalPayoutLedger
{
    /// <summary>Berilgan qatorlar uchun jami to'lov. Tashqi qator yo'q bo'lsa 0.</summary>
    decimal AmountFor(IEnumerable<SaleItem> items);

    /// <summary>
    /// Kassadan chiqimni yozadi: balansni kamaytiradi va Касса jurnaliga
    /// tushiradi. Saqlamaydi — chaqiruvchi o'z tranzaksiyasida saqlaydi.
    /// </summary>
    Task RecordAsync(Sale sale, CancellationToken cancellationToken = default);

    /// <summary>
    /// Teskarisi — sotuv bekor qilinganda. Tovar qo'shniga qaytariladi va pul
    /// kassaga qaytadi; mijozning puli ham qaytarilayotgani bilan simmetrik.
    /// </summary>
    Task ReverseAsync(Sale sale, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IExternalPayoutLedger"/>
public sealed class ExternalPayoutLedger : IExternalPayoutLedger
{
    private readonly IAppDbContext _db;
    private readonly ICashLedger _cashLedger;

    public ExternalPayoutLedger(IAppDbContext db, ICashLedger cashLedger)
    {
        _db = db;
        _cashLedger = cashLedger;
    }

    public decimal AmountFor(IEnumerable<SaleItem> items) =>
        items.Where(i => i.IsExternal).Sum(i => i.ExternalCostPrice * i.Quantity);

    public Task RecordAsync(Sale sale, CancellationToken cancellationToken = default) =>
        ApplyAsync(sale, outgoing: true, cancellationToken);

    public Task ReverseAsync(Sale sale, CancellationToken cancellationToken = default) =>
        ApplyAsync(sale, outgoing: false, cancellationToken);

    private async Task ApplyAsync(Sale sale, bool outgoing, CancellationToken cancellationToken)
    {
        // Qatorlarni o'zimiz o'qiymiz — chaqiruvchi SaleItems'ni yuklaganiga
        // bog'lanmaymiz (StockLedger bilan bir xil naqsh). Aks holda chiqim
        // jimgina 0 bo'lib qolar edi: masalan «qarzga» yo'lida sotuv
        // liniyalarsiz o'qiladi.
        var amount = await _db.SaleItems
            .Where(si => si.SaleId == sale.Id && si.IsExternal)
            .SumAsync(si => (decimal?)(si.ExternalCostPrice * si.Quantity), cancellationToken) ?? 0m;

        if (amount <= 0) return;

        // Local'dan boshlaymiz — chaqiruvchi (masalan SalePaymentService) shu
        // tranzaksiyada kassa qatorini yangi yaratgan, lekin hali SAQLAMAGAN
        // bo'lishi mumkin. Bazaga so'rov uni ko'rmaydi va biz ikkinchi
        // CashRegister qatorini yaratib qo'yardik: bir do'konda ikkita balans.
        var register = _db.CashRegisters.Local.FirstOrDefault(cr => cr.MarketId == sale.MarketId)
            ?? await _db.CashRegisters.FirstOrDefaultAsync(cr => cr.MarketId == sale.MarketId, cancellationToken);
        if (register is null)
        {
            // Sotuv naqd tushmasdan (to'liq qarzga) yakunlangan bo'lsa, kassa
            // qatori hali yaratilmagan bo'lishi mumkin — chiqim baribir yoziladi.
            register = new CashRegister
            {
                Id = Guid.NewGuid(),
                MarketId = sale.MarketId,
                CurrentBalance = 0,
            };
            _db.CashRegisters.Add(register);
        }

        register.CurrentBalance += outgoing ? -amount : amount;

        // Касса ro'yxatiga yozuv. Balansni bu emas, yuqoridagi CurrentBalance
        // belgilaydi — CashLedger shartnomasi shunday.
        // Ikkala yozuv turi bir xil, ishorasi bilan farqlanadi — qaytarishga
        // izoh qo'shamiz, aks holda ro'yxatda ikkita bir xil qator turgandek
        // ko'rinadi.
        _cashLedger.Record(sale.MarketId, outgoing ? -amount : amount,
            CashMovementType.ExternalPurchase,
            userId: sale.SellerId, shiftId: sale.ShiftId, refNumber: sale.SaleNumber,
            comment: outgoing ? null : "Sotuv bekor qilindi");
    }
}
