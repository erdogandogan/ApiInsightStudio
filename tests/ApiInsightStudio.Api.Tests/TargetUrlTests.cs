using ApiInsightStudio.Api.Notifications;
using ApiInsightStudio.Api.TestRunner;

namespace ApiInsightStudio.Api.Tests;

public class TargetUrlTests
{
    private static readonly string[] NoAllowed = Array.Empty<string>();

    // ----- Kök adres doğrulama -----

    [Theory]
    [InlineData("https://api.example.com")]
    [InlineData("https://api.example.com/")]
    [InlineData("https://api.example.com/v1")]
    [InlineData("https://api.example.com:8443/v1/")]
    public void Gecerli_https_kok_adresleri_kabul_edilir(string url)
    {
        Assert.True(TargetUrl.Validate(url, NoAllowed).Ok);
    }

    [Theory]
    [InlineData("https://api.example.com/v1?key=1")]
    [InlineData("https://api.example.com/v1?")]
    [InlineData("https://api.example.com/v1#bolum")]
    public void Sorgu_veya_parca_iceren_kok_adres_reddedilir(string url)
    {
        var check = TargetUrl.Validate(url, NoAllowed);

        Assert.False(check.Ok);
        Assert.Contains("sorgu", check.Error);
    }

    [Theory]
    [InlineData("http://api.example.com")]
    [InlineData("ftp://api.example.com")]
    [InlineData("https://user:pw@api.example.com")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bu bir adres degil")]
    public void SSRF_sozdizimi_kurallari_webhook_ile_ayni_sekilde_uygulanir(string? url)
    {
        Assert.False(TargetUrl.Validate(url, NoAllowed).Ok);
    }

    [Fact]
    public void Izin_listesindeki_host_icin_duz_http_kabul_edilir()
    {
        Assert.True(TargetUrl.Validate("http://127.0.0.1:5000", new[] { "127.0.0.1" }).Ok);
    }

    [Theory]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://10.0.0.5/api")]
    [InlineData("https://169.254.169.254/")]
    [InlineData("https://[::1]/api")]
    [InlineData("https://2130706433/api")]
    public async Task Ozel_adrese_cozulen_hedefler_kayit_aninda_reddedilir(string url)
    {
        var check = await TargetUrl.ValidateAsync(url, NoAllowed);

        Assert.False(check.Ok);
    }

    [Fact]
    public async Task Herkese_acik_IP_hedefi_kabul_edilir()
    {
        Assert.True((await TargetUrl.ValidateAsync("https://8.8.8.8/api", NoAllowed)).Ok);
    }

    // ----- Adres birleştirme -----

    private static Uri Base(string url) => new(url);

    [Theory]
    [InlineData("https://api.example.com", "/items", "https://api.example.com/items")]
    [InlineData("https://api.example.com/", "/items", "https://api.example.com/items")]
    [InlineData("https://api.example.com/v1", "/items", "https://api.example.com/v1/items")]
    [InlineData("https://api.example.com/v1/", "/items/0", "https://api.example.com/v1/items/0")]
    [InlineData("https://api.example.com:8443/v1", "/a/b", "https://api.example.com:8443/v1/a/b")]
    public void Kok_adres_ve_yol_dogru_birlestirilir(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, TargetUrl.Combine(Base(baseUrl), path)!.ToString());
    }

    [Theory]
    [InlineData("@evil.example.com/x")]       // kullanıcı bilgisi numarasıyla ana bilgisayar değiştirme
    [InlineData(":8080/x")]                   // port değiştirme
    [InlineData(".evil.example.com/x")]       // alt alan adı birleştirme
    public void Ana_bilgisayari_degistirmeye_calisan_yollar_reddedilir(string path)
    {
        Assert.Null(TargetUrl.Combine(Base("https://api.example.com"), path));
    }

    [Fact]
    public void Kok_yolun_disina_cikan_nokta_nokta_reddedilir()
    {
        Assert.Null(TargetUrl.Combine(Base("https://api.example.com/v1"), "/../admin"));
    }

    [Fact]
    public void Kok_yolun_ustunde_kalan_yol_kabul_edilir()
    {
        Assert.NotNull(TargetUrl.Combine(Base("https://api.example.com/v1"), "/items"));
    }
}
