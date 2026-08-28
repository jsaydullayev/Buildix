using System.Runtime.InteropServices;

namespace Buildix.Desktop;

/// <summary>
/// Baytlarni printerga XOM (RAW) holda yuboradi — drayver ularga
/// umuman tegmaydi.
///
/// <para><b>Nega kerak.</b> Termal printer o'z tilini (ESC/POS) tushunadi
/// va chekni o'zi chizadi. Odatdagi chop etish yo'lida esa hujjat avval
/// rasmga aylanadi, so'ng drayver uni qayta rasterlaydi — kassir tugmani
/// bosgach bir necha soniya kutadi. RAW rejimida baytlar to'g'ridan-to'g'ri
/// printerga boradi va qog'oz deyarli darhol chiqadi.</para>
///
/// <para><b>Nega TCP:9100 emas.</b> Uni ham qo'shsa bo'lardi, lekin u
/// FAQAT tarmoq printerida ishlaydi: USB bilan ulangan printerda IP
/// umuman yo'q. Windows navbati esa ikkalasini ham qamraydi — USB,
/// tarmoq, umumiy (shared) printer bir xil ishlaydi va printer allaqachon
/// NOMI bo'yicha tanlangan.</para>
/// </summary>
internal static class RawPrinter
{
    /// <summary>
    /// Yuboradi; muvaffaqiyatli bo'lsa <c>null</c>, aks holda sabab.
    /// </summary>
    public static string? Send(string printerName, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return "Printer tanlanmagan.";
        if (data.Length == 0) return "Yuboriladigan ma'lumot bo'sh.";

        if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            return $"«{printerName}» printeri ochilmadi (kod {Marshal.GetLastWin32Error()}).";

        var buffer = IntPtr.Zero;
        try
        {
            var info = new DOCINFO
            {
                pDocName = "Buildix chek",
                pOutputFile = null,
                // RAW — «bu tayyor ma'lumot, unga tegma» degani. Boshqa tur
                // berilsa drayver baytlarni matn deb qabul qilar va ESC/POS
                // buyruqlari qog'ozga o'zi bosilib chiqardi.
                pDataType = "RAW",
            };

            if (!StartDocPrinter(printer, 1, info))
                return $"Chop etish boshlanmadi (kod {Marshal.GetLastWin32Error()}).";

            try
            {
                if (!StartPagePrinter(printer))
                    return $"Sahifa boshlanmadi (kod {Marshal.GetLastWin32Error()}).";

                buffer = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, buffer, data.Length);

                if (!WritePrinter(printer, buffer, data.Length, out var written))
                    return $"Yozib bo'lmadi (kod {Marshal.GetLastWin32Error()}).";
                if (written != data.Length)
                    return $"Ma'lumot to'liq yozilmadi ({written}/{data.Length} bayt).";

                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DOCINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocName = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDataType = "RAW";
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string src, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr printer, int level,
        [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFO di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr buf, int count, out int written);
}
