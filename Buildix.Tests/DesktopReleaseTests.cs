using Buildix.Application.Common;

namespace Buildix.Tests;

/// <summary>
/// Sozlamalar sahifasidagi «versiya 1.0.3» yozuvi qayerdan olinishi.
///
/// <para>Ilgari u <c>.env</c> da qo'lda turardi va papkadagi paket bilan
/// ajralib qolishi mumkin edi — sahifa egaga bir raqamni ko'rsatar,
/// yuklab olingan fayl esa boshqasi bo'lardi. Xato hech qanday belgi
/// bermasdi, shuning uchun manba bitta qilindi: paketlar ro'yxatining
/// o'zi.</para>
/// </summary>
public class DesktopReleaseTests
{
    private const string Releases = """
    {"Assets":[
      {"PackageId":"Buildix","Version":"1.0.1","Type":"Full","FileName":"a.nupkg"},
      {"PackageId":"Buildix","Version":"1.0.3","Type":"Full","FileName":"b.nupkg"},
      {"PackageId":"Buildix","Version":"1.0.3","Type":"Delta","FileName":"c.nupkg"}
    ]}
    """;

    [Fact]
    public void Eng_yangi_versiya_tanlanadi()
    {
        Assert.Equal("1.0.3", DesktopRelease.VersionFromReleases(Releases));
    }

    /// <summary>
    /// Matn bo'yicha solishtirilsa "1.0.10" < "1.0.9" bo'lib chiqadi va
    /// sahifa eski versiyani ko'rsatib turardi — o'ninchi chiqarishdan
    /// keyin, ya'ni sabab allaqachon unutilgan paytda.
    /// </summary>
    [Fact]
    public void Raqam_boyicha_solishtiriladi_matn_boyicha_emas()
    {
        var json = """
        {"Assets":[
          {"Version":"1.0.9","Type":"Full"},
          {"Version":"1.0.10","Type":"Full"}
        ]}
        """;

        Assert.Equal("1.0.10", DesktopRelease.VersionFromReleases(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bu json emas")]
    [InlineData("{}")]
    [InlineData("""{"Assets":[]}""")]
    [InlineData("""{"Assets":"satr"}""")]
    [InlineData("""{"Assets":[{"Type":"Full"}]}""")]
    [InlineData("""{"Assets":[{"Version":"nom","Type":"Full"}]}""")]
    public void Yaroqsiz_royxat_null_qaytaradi(string? json)
    {
        Assert.Null(DesktopRelease.VersionFromReleases(json));
    }

    [Fact]
    public void Manzildan_papka_ajratiladi()
    {
        Assert.Equal("a1b2c3", DesktopRelease.FolderFromUrl(
            "https://buildix.uz/updates/a1b2c3/Buildix-win-Setup.exe"));
    }

    [Fact]
    public void Soro_va_langar_tashlanadi()
    {
        Assert.Equal("a1b2c3", DesktopRelease.FolderFromUrl(
            "https://buildix.uz/updates/a1b2c3/Setup.exe?v=2#top"));
    }

    /// <summary>
    /// Qiymat sozlamadan keladi va fayl yo'liga qo'shiladi — ya'ni u orqali
    /// papkadan chiqib ketishga urinish mumkin bo'lmasligi kerak.
    /// </summary>
    [Theory]
    [InlineData("https://buildix.uz/updates/../Setup.exe")]
    [InlineData("https://buildix.uz/updates/./Setup.exe")]
    [InlineData("https://buildix.uz/updates/c:windows/Setup.exe")]
    [InlineData("https://buildix.uz/updates/a\\b/Setup.exe")]
    public void Papkadan_chiqishga_urinish_rad_etiladi(string url)
    {
        Assert.Null(DesktopRelease.FolderFromUrl(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Setup.exe")]
    public void Yaroqsiz_manzil_null_qaytaradi(string? url)
    {
        Assert.Null(DesktopRelease.FolderFromUrl(url));
    }
}
