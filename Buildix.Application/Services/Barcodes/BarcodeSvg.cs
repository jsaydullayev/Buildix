using System.Text;
using ZXing.OneD;

namespace Buildix.Application.Services.Barcodes;

/// <summary>
/// EAN-13 chiziqlarini SVG ga aylantiradi.
///
/// <para><b>Nega ZXing.</b> EAN-13 kodlash qoidalari qat'iy belgilangan, lekin
/// qo'lda yozilgan kodlagichdagi nozik xato faqat printer sotib olinib, yuzlab
/// yorliq bosilgandan keyin bilinardi — skaner shunchaki o'qimay qo'yadi.
/// ZXing sinovdan o'tgan; biz undan faqat modul naqshini olamiz.</para>
///
/// <para><b>Nega SVG, rasm emas.</b> Yorliq printerlari 203-300 dpi da bosadi.
/// Rastr rasm o'sha o'lchamda chetlari xiralashadi va skaner ba'zi kodlarni
/// o'qimay qoladi. SVG vektor — qanday o'lchamda ham chiziqlar aniq qoladi.
/// QuestPDF ni SVG bilan ishlatish loyihada allaqachon sinalgan (brend belgisi).</para>
/// </summary>
public static class BarcodeSvg
{
    /// <summary>
    /// EAN-13 uchun SVG. <paramref name="widthMm"/>/<paramref name="heightMm"/> —
    /// chiziqlar maydonining o'lchami; raqamlar alohida, PDF tomonida yoziladi.
    /// </summary>
    public static string Ean13(string code, double widthMm, double heightMm)
    {
        if (!Barcodes.Ean13.IsValid(code))
            throw new ArgumentException($"'{code}' yaroqli EAN-13 emas.", nameof(code));

        // ZXing 0 kenglik so'ralganda minimal (95 modul) naqshni qaytaradi —
        // aynan shu kerak: masshtabni SVG viewBox o'zi hal qiladi.
        var matrix = new EAN13Writer().encode(code, ZXing.BarcodeFormat.EAN_13, 0, 1);
        var modules = matrix.Width;

        var sb = new StringBuilder(1024);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
          .Append(modules).Append(" 100\" width=\"").Append(Mm(widthMm))
          .Append("mm\" height=\"").Append(Mm(heightMm))
          .Append("mm\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\">");

        // Ketma-ket qora modullar bitta to'rtburchakka birlashtiriladi: 95 ta
        // alohida <rect> o'rniga ~30 ta chiqadi va PDF yengilroq bo'ladi.
        var x = 0;
        while (x < modules)
        {
            if (!matrix[x, 0]) { x++; continue; }
            var start = x;
            while (x < modules && matrix[x, 0]) x++;
            sb.Append("<rect x=\"").Append(start).Append("\" y=\"0\" width=\"")
              .Append(x - start).Append("\" height=\"100\" fill=\"#000\"/>");
        }

        return sb.Append("</svg>").ToString();
    }

    private static string Mm(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
