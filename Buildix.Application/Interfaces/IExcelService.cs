namespace Buildix.Application.Interfaces;

public interface IExcelService
{
    byte[] GenerateExcel<T>(IEnumerable<T> data, string sheetName = "Sheet1");
}
