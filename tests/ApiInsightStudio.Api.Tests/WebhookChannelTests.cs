using System.Net;
using System.Text.Json;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Sahte HTTP ile webhook kanalının isteği, imzayı ve hata sınıflandırmasını doğrular.</summary>
public class WebhookChannelTests
{
    private const string Secret = "whsec_test_secret";
    private const string Url = "https://hooks.example.com/alerts";

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request);
        }
    }

    private readonly WebhookSecretProtector _protector = new(new EphemeralDataProtectionProvider());
    private readonly TestClock _clock = new();

    private WebhookChannel CreateChannel(CapturingHandler handler, params string[] allowedPrivateHosts) =>
        new(new FakeHttpClientFactory(handler), _protector, _clock,
            Options.Create(new NotificationOptions { AllowedPrivateHosts = allowedPrivateHosts }));

    private static AlertRaised Alert(Guid? eventId = null) =>
        new(7, AlertRuleCodes_LowScore, "Kalite skoru 50, eşik değer (60) altında.", "Medium",
            new DateTime(2026, 10, 4, 11, 59, 0, DateTimeKind.Utc), eventId ?? Guid.NewGuid());

    private const string AlertRuleCodes_LowScore = "LOW_SCORE";

    private NotificationContext Context(AlertRaised? alert = null, string? url = Url, string? protectedSecret = "default") =>
        new(7, "Proje Adı", alert ?? Alert(), new ProjectAutomationSettings
        {
            ProjectId = 7,
            WebhookUrl = url,
            WebhookSecretProtected = protectedSecret == "default" ? _protector.Protect(Secret) : protectedSecret
        });

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    // ----- İstek biçimi -----

    [Fact]
    public async Task Imzali_JSON_POST_gonderir_ve_2xx_basarili_sayilir()
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));
        var alert = Alert();

        var result = await CreateChannel(handler).SendAsync(Context(alert), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(Url, handler.Request.RequestUri!.ToString());
        Assert.StartsWith("application/json", handler.Request.Content!.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Basliklar_olay_kimligi_zaman_damgasi_ve_gecerli_imza_icerir()
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));
        var alert = Alert();

        await CreateChannel(handler).SendAsync(Context(alert), CancellationToken.None);

        var headers = handler.Request!.Headers;
        var timestamp = headers.GetValues("X-Timestamp").Single();
        Assert.Equal(_clock.GetUtcNow().ToUnixTimeSeconds().ToString(), timestamp);
        Assert.Equal(alert.EventId.ToString(), headers.GetValues("X-Event-Id").Single());
        Assert.StartsWith("ApiInsightStudio-Webhook/", headers.GetValues("User-Agent").Single());

        // Alıcı, kendi sırrıyla aynı imzayı hesaplayabilmeli
        var expected = WebhookSigner.HeaderValue(Secret, long.Parse(timestamp), handler.Body!);
        Assert.Equal(expected, headers.GetValues("X-Signature").Single());
    }

    [Fact]
    public async Task Govde_olayi_proje_ve_uyari_bilgisini_tasir_ve_sir_icermez()
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));
        var alert = Alert();

        await CreateChannel(handler).SendAsync(Context(alert), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal(alert.EventId.ToString(), root.GetProperty("id").GetString());
        Assert.Equal("alert.raised", root.GetProperty("type").GetString());
        Assert.Equal(7, root.GetProperty("project").GetProperty("id").GetInt32());
        Assert.Equal("Proje Adı", root.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal("LOW_SCORE", root.GetProperty("alert").GetProperty("ruleCode").GetString());
        Assert.Equal("Medium", root.GetProperty("alert").GetProperty("severity").GetString());
        Assert.DoesNotContain(Secret, handler.Body);
    }

    // ----- Sonuç sınıflandırma -----

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Tum_2xx_kodlari_basarilidir(HttpStatusCode code)
    {
        var result = await CreateChannel(new CapturingHandler(_ => Status(code))).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Found)]               // yönlendirme izlenmez, başarısız sayılır
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task Diger_durum_kodlari_gecici_hata_sayilir_ve_kodu_kaydeder(HttpStatusCode code)
    {
        var result = await CreateChannel(new CapturingHandler(_ => Status(code))).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Equal((int)code, result.StatusCode);
        Assert.Contains(((int)code).ToString(), result.Error);
    }

    [Fact]
    public async Task Baglanti_hatasi_gecicidir_ve_adresi_hata_metnine_koymaz()
    {
        const string urlWithToken = "https://hooks.example.com/alerts?token=COK-GIZLI-TOKEN";
        var handler = new CapturingHandler(_ => throw new HttpRequestException(
            "baglanti hatasi " + urlWithToken, new System.Net.Sockets.SocketException()));

        var result = await CreateChannel(handler).SendAsync(Context(url: urlWithToken), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Null(result.StatusCode);
        Assert.DoesNotContain("COK-GIZLI-TOKEN", result.Error);
        Assert.DoesNotContain("hooks.example.com", result.Error);
    }

    [Fact]
    public async Task Zaman_asimi_gecici_hatadir()
    {
        var handler = new CapturingHandler(_ => throw new TaskCanceledException("zaman aşımı"));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Contains("Zaman aşımı", result.Error);
    }

    [Fact]
    public async Task Dis_iptal_istegi_zaman_asimi_gibi_yutulmaz()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = new CapturingHandler(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateChannel(handler).SendAsync(Context(), cts.Token));
    }

    [Fact]
    public async Task Guvensiz_hedef_baglanti_engeli_kalici_hatadir()
    {
        var handler = new CapturingHandler(_ => throw new HttpRequestException(
            "genel hata", new UnsafeTargetException("Hedef adres özel/yerel bir ağa çözülüyor; bağlantı engellendi.")));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsPermanent);
        Assert.Contains("engellendi", result.Error);
    }

    // ----- Gönderim anı doğrulamaları (hepsi kalıcı hata, hiç istek atılmaz) -----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adres_kaldirilmissa_kalici_hata_ve_istek_atilmaz(string? url)
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));

        var result = await CreateChannel(handler).SendAsync(Context(url: url), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://hooks.example.com/alerts")]       // düz http
    [InlineData("ftp://hooks.example.com/alerts")]
    [InlineData("https://user:pass@hooks.example.com/")]
    public async Task Artik_gecersiz_adres_kalici_hata_ve_istek_atilmaz(string url)
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));

        var result = await CreateChannel(handler).SendAsync(Context(url: url), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Izin_listesindeki_host_icin_duz_http_gonderilir()
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));

        var result = await CreateChannel(handler, "localhost")
            .SendAsync(Context(url: "http://localhost:5000/hook"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Sir_yoksa_kalici_hata_ve_istek_atilmaz(string? protectedSecret)
    {
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));

        var result = await CreateChannel(handler).SendAsync(Context(protectedSecret: protectedSecret), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Contains("sır", result.Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Cozulemeyen_sir_kalici_hata_ve_sirri_yenile_der()
    {
        var other = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        var handler = new CapturingHandler(_ => Status(HttpStatusCode.OK));

        var result = await CreateChannel(handler)
            .SendAsync(Context(protectedSecret: other.Protect(Secret)), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Contains("yenileyin", result.Error);
        Assert.Equal(0, handler.Calls);
    }
}
