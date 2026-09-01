namespace Buildix.Application.Interfaces;

/// <summary>
/// Bitta tovar bo'yicha qoldiq va JURNAL o'rtasidagi farq.
/// </summary>
/// <param name="Stored">Tovar qatoridagi saqlangan qoldiq.</param>
/// <param name="FromLedger">Ombor jurnalidagi harakatlar yig'indisi.</param>
/// <param name="Reserved">Ochiq qoralama cheklar ushlab turgan miqdor.</param>
/// <param name="Drift">
/// <c>Stored − (FromLedger − Reserved)</c>. Nol bo'lishi SHART.
/// </param>
public readonly record struct StockDrift(
    Guid ProductId,
    string ProductName,
    decimal Stored,
    decimal FromLedger,
    decimal Reserved,
    decimal Drift);

/// <summary>
/// Ombor qoldig'ini JURNALDAN qayta hisoblaydi va siljishni topadi.
/// </summary>
/// <remarks>
/// <para><b>Nega kerak.</b> Bugun haqiqat manbai — <c>Product.Quantity</c>
/// ustuni, jurnal esa uni tavsiflaydi. Bitta bazada bu ishlaydi. Ikkita
/// mustaqil kassa bir do'kon nomidan ishlay boshlaganda esa ishlamaydi:
/// ikkalasi ham o'z ustunini o'zgartiradi va bulut qatorni ID bo'yicha
/// ustiga yozganda ARIFMETIKA yo'qoladi — 3 sotgan va 2 sotgan kassadan
/// 5 emas, oxirgi yuborganning raqami qoladi.</para>
///
/// <para>Yechim — haqiqat manbaini jurnalga ko'chirish: jurnal qo'shiladigan
/// (append-only) va uni birlashtirish uchun hech qanday nizo qoidasi kerak
/// emas, ikkala kassaning qatorlari shunchaki qo'shiladi. Bu xizmat o'sha
/// ko'chishning birinchi qadami: ustun bilan jurnal AYNAN mos kelishini
/// o'lchaydi.</para>
///
/// <para><b>Qoida sodda «Quantity = SUM(Delta)» EMAS.</b> Savat qurish
/// paytida qoldiq kamayadi, lekin jurnalga yozilmaydi — qoralama churn'i
/// (qo'shdi, o'chirdi, yana qo'shdi) tarixni ifloslantirmasligi kerak.
/// Jurnalga bitta yozuv chek YAKUNLANGANDA tushadi
/// (<see cref="IStockLedger.RecordSaleFinalizationAsync"/>). Shuning uchun
/// ochiq qoralamalar ushlab turgan miqdor alohida hisobga olinadi:</para>
///
/// <code>Quantity == SUM(jurnal Delta) − (ochiq qoralamalardagi miqdor)</code>
/// </remarks>
public interface IStockReconciler
{
    /// <summary>
    /// Qoidaga bo'ysunmaydigan tovarlarni qaytaradi. Bo'sh ro'yxat — hammasi joyida.
    /// </summary>
    Task<IReadOnlyList<StockDrift>> FindDriftAsync(int marketId, CancellationToken ct = default);
}
