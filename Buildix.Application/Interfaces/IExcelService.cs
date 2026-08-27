namespace Buildix.Application.Interfaces;

public interface IExcelService
{
    byte[] GenerateExcel<T>(IEnumerable<T> data, string sheetName = "Sheet1");

    /// <summary>
    /// Sarlavhalar ATAYLAB beriladigan variant.
    /// </summary>
    /// <remarks>
    /// <para>Yuqoridagi umumiy variant sarlavhani anonim tipning XOSSA
    /// NOMIDAN oladi. Bu ikki narsani cheklaydi: nomda bo'shliq bo'la
    /// olmaydi (shu sababli fayllarda «Ed_izm», «Kam_qoldi» kabi ustunlar
    /// chiqardi) va matnni tilga qarab almashtirib bo'lmaydi — har til
    /// uchun butun proyeksiyani qaytadan yozish kerak edi.</para>
    ///
    /// <para>Bu yerda sarlavha ham, qatorlar ham oddiy ro'yxat, ya'ni til
    /// faqat chaqiruvchida tanlanadi.</para>
    /// </remarks>
    byte[] GenerateExcel(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        string sheetName = "Sheet1");
}
