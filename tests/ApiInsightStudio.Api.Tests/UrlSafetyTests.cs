using System.Net;
using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.Tests;

/// <summary>SSRF koruması: iç ağa, loopback'e ve bulut metadata adresine bildirim gönderilememeli.</summary>
public class UrlSafetyTests
{
    private static readonly string[] NoAllowedHosts = Array.Empty<string>();

    // ----- IP sınıflandırma -----

    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]           // özel
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]    // bulut metadata
    [InlineData("169.254.0.1")]        // link-local
    [InlineData("100.64.0.1")]         // CGNAT
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]          // dokümantasyon
    [InlineData("198.18.0.1")]         // test
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("240.0.0.1")]          // ayrılmış
    [InlineData("255.255.255.255")]    // broadcast
    public void Ozel_yerel_ve_ayrilmis_IPv4_adresleri_herkese_acik_degildir(string ip)
    {
        Assert.False(UrlSafety.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")]     // 172.16/12'nin hemen dışı
    [InlineData("172.32.0.1")]         // 172.16/12'nin hemen dışı
    [InlineData("100.63.255.255")]     // CGNAT'ın hemen dışı
    [InlineData("100.128.0.1")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.1")]
    [InlineData("203.0.114.1")]
    [InlineData("223.255.255.255")]
    public void Herkese_acik_IPv4_adresleri_kabul_edilir(string ip)
    {
        Assert.True(UrlSafety.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("::1")]                       // loopback
    [InlineData("::")]
    [InlineData("fe80::1")]                   // link-local
    [InlineData("fc00::1")]                   // unique local
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fec0::1")]                   // site-local
    [InlineData("ff02::1")]                   // multicast
    [InlineData("2001:db8::1")]               // dokümantasyon
    [InlineData("2001::1")]                   // Teredo
    [InlineData("::ffff:127.0.0.1")]          // IPv4-mapped loopback
    [InlineData("::ffff:10.0.0.1")]           // IPv4-mapped özel
    [InlineData("::ffff:169.254.169.254")]    // IPv4-mapped metadata
    [InlineData("64:ff9b::7f00:1")]           // NAT64 ile 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")]        // NAT64 ile 169.254.169.254
    [InlineData("2002:7f00:1::1")]            // 6to4 ile 127.0.0.1
    [InlineData("2002:a9fe:a9fe::1")]         // 6to4 ile 169.254.169.254
    [InlineData("::10.0.0.1")]                // IPv4 uyumlu
    public void Ozel_yerel_ve_gomulu_ozel_IPv6_adresleri_herkese_acik_degildir(string ip)
    {
        Assert.False(UrlSafety.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]      // Cloudflare
    [InlineData("2001:4860:4860::8888")]      // Google
    [InlineData("::ffff:8.8.8.8")]            // IPv4-mapped herkese açık
    [InlineData("64:ff9b::808:808")]          // NAT64 ile 8.8.8.8
    [InlineData("2002:808:808::1")]           // 6to4 ile 8.8.8.8
    public void Herkese_acik_IPv6_adresleri_kabul_edilir(string ip)
    {
        Assert.True(UrlSafety.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("2130706433")]    // 127.0.0.1 ondalık
    [InlineData("0x7f000001")]    // 127.0.0.1 onaltılık
    [InlineData("017700000001")]  // 127.0.0.1 sekizlik
    [InlineData("127.1")]         // kısaltılmış loopback
    public void Kod_degistirilmis_loopback_yazimlari_da_loopback_olarak_cozulur(string host)
    {
        // Adres, IP'ye çözüldükten sonra doğrulandığı için bu gizleme yöntemleri işe yaramaz.
        var addresses = UrlSafety.ResolveAsync(host, CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.False(UrlSafety.IsPublicAddress(address)));
    }

    // ----- Sözdizimi -----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@example.com/hook")]
    [InlineData("https://user@example.com/hook")]
    [InlineData("http://example.com/hook")]              // düz http, izin listesinde değil
    public void Gecersiz_ya_da_guvensiz_sozdizimleri_reddedilir(string? url)
    {
        var check = UrlSafety.ValidateSyntax(url, NoAllowedHosts);

        Assert.False(check.Ok);
        Assert.False(string.IsNullOrWhiteSpace(check.Error));
    }

    [Fact]
    public void Cok_uzun_adres_reddedilir()
    {
        var url = "https://example.com/" + new string('a', 2100);

        Assert.False(UrlSafety.ValidateSyntax(url, NoAllowedHosts).Ok);
    }

    [Theory]
    [InlineData("https://example.com/hook")]
    [InlineData("https://example.com:8443/hook?x=1")]
    [InlineData("HTTPS://EXAMPLE.COM/hook")]
    public void Gecerli_https_adresleri_kabul_edilir(string url)
    {
        var check = UrlSafety.ValidateSyntax(url, NoAllowedHosts);

        Assert.True(check.Ok);
        Assert.NotNull(check.Uri);
    }

    [Fact]
    public void Izin_listesindeki_host_icin_duz_http_kabul_edilir()
    {
        var check = UrlSafety.ValidateSyntax("http://localhost:5000/hook", new[] { "localhost" });

        Assert.True(check.Ok);
    }

    [Fact]
    public void Izin_listesi_buyuk_kucuk_harfe_duyarsizdir()
    {
        Assert.True(UrlSafety.ValidateSyntax("http://LocalHost:5000/hook", new[] { "localhost" }).Ok);
    }

    [Fact]
    public void Izin_listesi_baska_hostlari_acmaz()
    {
        Assert.False(UrlSafety.ValidateSyntax("http://example.com/hook", new[] { "localhost" }).Ok);
    }

    // ----- Çözümleme (IP sabitleriyle, ağa çıkmadan) -----

    [Theory]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://172.16.5.4/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[fe80::1]/hook")]
    [InlineData("https://[::ffff:127.0.0.1]/hook")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("https://2130706433/hook")]
    [InlineData("https://0x7f000001/hook")]
    public async Task Ozel_adrese_cozulen_adresler_reddedilir(string url)
    {
        var check = await UrlSafety.ValidateAsync(url, NoAllowedHosts);

        Assert.False(check.Ok);
        Assert.Contains("özel", check.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://8.8.8.8/hook")]
    [InlineData("https://[2606:4700:4700::1111]/hook")]
    public async Task Herkese_acik_IP_adresleri_kabul_edilir(string url)
    {
        var check = await UrlSafety.ValidateAsync(url, NoAllowedHosts);

        Assert.True(check.Ok);
    }

    [Fact]
    public async Task Izin_listesindeki_host_IP_kontrolunden_muaftir()
    {
        var check = await UrlSafety.ValidateAsync("http://127.0.0.1:5000/hook", new[] { "127.0.0.1" });

        Assert.True(check.Ok);
    }

    [Fact]
    public async Task Izin_listesinde_olmayan_loopback_http_ile_de_reddedilir()
    {
        var check = await UrlSafety.ValidateAsync("http://127.0.0.1:5000/hook", NoAllowedHosts);

        Assert.False(check.Ok);
    }

    [Fact]
    public async Task Cozumlenemeyen_host_reddedilir()
    {
        var check = await UrlSafety.ValidateAsync("https://bu-host-kesinlikle-yok.invalid/hook", NoAllowedHosts);

        Assert.False(check.Ok);
        Assert.Contains("çözümlenemedi", check.Error);
    }
}
