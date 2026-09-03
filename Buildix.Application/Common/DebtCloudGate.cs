using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Common;

/// <summary>
/// «Qarz amallari uchun bulut bilan aloqa kerak» qoidasi.
/// </summary>
/// <remarks>
/// <para><b>Qanday xavfdan himoya qiladi.</b> Ikkita kassa o'z bazasi bilan
/// ishlaganda ular bir-birining qarz yozuvlarini KO'RMAYDI. Ikkalasi ham
/// oflayn holda:</para>
/// <list type="bullet">
///   <item>bitta mijozga qarz bera oladi — chegara ikki marta sarflanadi;</item>
///   <item>bitta qarzni ikki marta undira oladi;</item>
///   <item>bitta avansni ikki marta sarflay oladi.</item>
/// </list>
/// <para>Bularning hech biri xato bermaydi: raqamlar keyin, birlashganda
/// to'g'ri kelmay qoladi va sababini topish deyarli imkonsiz.</para>
///
/// <para><b>Nega FAQAT qarz.</b> Naqd va karta savdosi oflayn ham xavfsiz —
/// pul o'sha yerda, o'sha zahoti olinadi va uni ikki marta olib bo'lmaydi.
/// Qarz esa va'da: uning to'g'riligi BOSHQA yozuvlarga bog'liq va o'sha
/// yozuvlar boshqa kassada bo'lishi mumkin.</para>
///
/// <para><b>Sukut bo'yicha O'CHIQ</b> va bu ataylab: bitta bazali do'konda
/// (bugungi holat, ikkita LAN kassasi ham shunga kiradi) qarz yozuvi bitta
/// joyda turadi va u har doim o'ziga o'zi mos. Qoidani yoqish faqat
/// mustaqil bazali kassalar paydo bo'lganda ma'noga ega.</para>
/// </remarks>
public static class DebtCloudGate
{
    /// <summary>Kassirga ko'rinadigan matn — sabab va nima qilish kerakligi.</summary>
    public const string Message =
        "Qarz amallari uchun bulut bilan aloqa kerak — hozir ma'lumot eskirgan. "
        + "Naqd yoki karta bilan rasmiylashtiring, yoki aloqa tiklanganda qaytadan urining.";

    /// <summary>Xato kodi — mijoz uni alohida ekran bilan ko'rsatishi mumkin.</summary>
    public const string Code = "DEBT_NEEDS_CLOUD";

    /// <summary>
    /// Amal to'silishi kerakmi.
    /// </summary>
    /// <remarks>
    /// <para>Yangilik o'lchovi <see cref="ISyncFreshnessService"/> dan
    /// olinadi — u do'kon va bulut uchun ikki xil savolga javob berishni
    /// allaqachon biladi, ya'ni bu yerda uni qaytadan yozish ikkita zid
    /// ta'rif hosil qilardi.</para>
    ///
    /// <para>Bog'lanmagan do'kon ham to'siladi: kalitsiz kassa hech qachon
    /// boshqa kassaning yozuvini ko'ra olmaydi, ya'ni u doimiy «oflayn».</para>
    /// </remarks>
    public static bool Blocks(MarketSettings settings, SyncFreshnessDto freshness)
    {
        if (!settings.DebtRequiresCloud) return false;

        return !freshness.IsPaired
            || !freshness.IsFresh
            || freshness.Error is not null;
    }
}
