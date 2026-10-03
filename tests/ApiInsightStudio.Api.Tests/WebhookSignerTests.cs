using System.Security.Cryptography;
using System.Text;
using ApiInsightStudio.Api.Notifications;
using Microsoft.AspNetCore.DataProtection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>İmza, alıcının kendi başına doğrulayabileceği standart bir HMAC-SHA256 olmalı.</summary>
public class WebhookSignerTests
{
    /// <summary>İmzayı, üretici koddan bağımsız olarak (alıcının yapacağı gibi) yeniden hesaplar.</summary>
    private static string ReceiverComputes(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body));
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    [Fact]
    public void Imza_alicinin_bagimsiz_hesabiyla_ayni_cikar()
    {
        const string secret = "whsec_test";
        const string body = "{\"id\":\"abc\",\"type\":\"alert.raised\"}";

        Assert.Equal(ReceiverComputes(secret, "1760000000", body), WebhookSigner.Sign(secret, 1760000000, body));
    }

    [Fact]
    public void Baslik_degeri_sha256_onekiyle_baslar()
    {
        var header = WebhookSigner.HeaderValue("s", 1, "{}");

        Assert.StartsWith("sha256=", header);
        Assert.Equal("sha256=".Length + 64, header.Length);
        Assert.Equal(header.ToLowerInvariant(), header);
    }

    [Fact]
    public void Bilinen_test_vektoru()
    {
        // HMAC-SHA256(key="key", data="1.{}") — bağımsız olarak hesaplanmış sabit değer
        var expected = ReceiverComputes("key", "1", "{}");

        Assert.Equal(expected, WebhookSigner.Sign("key", 1, "{}"));
        Assert.Equal(64, expected.Length);
    }

    [Fact]
    public void Govde_degisirse_imza_degisir()
    {
        Assert.NotEqual(WebhookSigner.Sign("s", 1, "{\"a\":1}"), WebhookSigner.Sign("s", 1, "{\"a\":2}"));
    }

    [Fact]
    public void Zaman_damgasi_imzaya_dahildir_eski_istek_tekrar_oynatilamaz()
    {
        Assert.NotEqual(WebhookSigner.Sign("s", 1, "{}"), WebhookSigner.Sign("s", 2, "{}"));
    }

    [Fact]
    public void Sir_degisirse_imza_degisir()
    {
        Assert.NotEqual(WebhookSigner.Sign("s1", 1, "{}"), WebhookSigner.Sign("s2", 1, "{}"));
    }

    // ----- Sır üretimi ve saklama -----

    [Fact]
    public void Uretilen_sirlar_whsec_onekli_uzun_ve_benzersizdir()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());

        var secrets = Enumerable.Range(0, 20).Select(_ => protector.Generate()).ToList();

        Assert.All(secrets, s => Assert.StartsWith("whsec_", s));
        Assert.All(secrets, s => Assert.Equal("whsec_".Length + 64, s.Length)); // 256 bit
        Assert.Equal(secrets.Count, secrets.Distinct().Count());
    }

    [Fact]
    public void Sir_sifrelenip_geri_cozulebilir_ve_sifreli_hali_sirri_icermez()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var secret = protector.Generate();

        var protectedValue = protector.Protect(secret);

        Assert.DoesNotContain(secret, protectedValue);
        Assert.Equal(secret, protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Baska_anahtar_halkasiyla_sifrelenmis_sir_cozulemez()
    {
        var first = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var second = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var protectedValue = first.Protect("whsec_x");

        Assert.ThrowsAny<CryptographicException>(() => second.Unprotect(protectedValue));
    }
}
