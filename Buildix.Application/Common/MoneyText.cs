using System.Globalization;

namespace Buildix.Application.Common;

/// <summary>
/// Xabarlardagi pul summasi.
///
/// <para><b>Nega alohida.</b> <c>value:N0</c> joriy MADANIYATGA tayanadi:
/// bitta mashinada «663 000», boshqasida «663,000» chiqadi. Ikkinchisi
/// o'zbek o'quvchisi uchun chalg'ituvchi — bu yerda vergul kasr ajratkichi
/// va summa olti yuz baravar kichik ko'rinadi. Server madaniyati esa
/// sozlamaga, muhitga va konteyner obraziga bog'liq, ya'ni uni kod
/// bo'ylab bir xil deb hisoblab bo'lmaydi.</para>
///
/// <para>Shu sababli ajratkich shu yerda QAT'IY belgilanadi va xabar
/// hamma joyda bir xil ko'rinadi.</para>
/// </summary>
public static class MoneyText
{
    /// <summary>Masalan 663000 → «663 000».</summary>
    public static string Sum(decimal value) =>
        value.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
