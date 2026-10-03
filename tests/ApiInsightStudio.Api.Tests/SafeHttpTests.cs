using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Gerçek bir yerel HTTP dinleyicisiyle SSRF korumasını uçtan uca doğrular: bağlantı kurulmadan engellenmeli,
/// yönlendirme izlenmemeli, izin verilen host için imzalı istek alıcıya ulaşıp doğrulanabilmeli.
/// </summary>
public sealed class SafeHttpTests
{
    private sealed record CapturedRequest(string Path, IReadOnlyDictionary<string, string> Headers, string Body);

    /// <summary>127.0.0.1 üzerinde rastgele bir portta dinleyen, gelen istekleri kaydeden küçük sunucu.</summary>
    private sealed class LocalListener : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, (int Status, string? Location)> _respond;

        public int Port { get; }
        public List<CapturedRequest> Requests { get; } = new();

        public LocalListener(Func<HttpListenerRequest, (int, string?)>? respond = null)
        {
            _respond = respond ?? (_ => (200, null));

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    break;
                }

                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var headers = context.Request.Headers.AllKeys
                    .Where(k => k is not null)
                    .ToDictionary(k => k!, k => context.Request.Headers[k] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                lock (Requests)
                    Requests.Add(new CapturedRequest(context.Request.Url!.AbsolutePath, headers, body));

                var (status, location) = _respond(context.Request);
                context.Response.StatusCode = status;
                if (location is not null)
                    context.Response.RedirectLocation = location;
                context.Response.Close();
            }
        }

        public int RequestCount { get { lock (Requests) return Requests.Count; } }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private static HttpClient SafeClient(params string[] allowedPrivateHosts) =>
        new(SafeHttp.CreateHandler(allowedPrivateHosts)) { Timeout = TimeSpan.FromSeconds(5) };

    // ----- Güvenli işleyici -----

    [Fact]
    public async Task Izin_listesinde_olmayan_loopback_adresine_baglanti_kurulmadan_engellenir()
    {
        using var listener = new LocalListener();
        using var client = SafeClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync($"http://127.0.0.1:{listener.Port}/"));

        Assert.IsType<UnsafeTargetException>(ex.InnerException);
        Assert.Equal(0, listener.RequestCount); // istek sunucuya hiç ulaşmadı
    }

    [Fact]
    public async Task Localhost_ismi_loopback_IPye_cozuldugu_icin_engellenir()
    {
        using var listener = new LocalListener();
        using var client = SafeClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync($"http://localhost:{listener.Port}/"));

        Assert.IsType<UnsafeTargetException>(ex.InnerException);
        Assert.Equal(0, listener.RequestCount);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]   // bulut metadata
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://2130706433/")]                         // 127.0.0.1 ondalık yazım
    [InlineData("http://0x7f000001/")]                         // 127.0.0.1 onaltılık yazım
    public async Task Ozel_adresler_baglanti_denemesi_yapilmadan_engellenir(string url)
    {
        using var client = SafeClient();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));

        Assert.IsType<UnsafeTargetException>(ex.InnerException);
    }

    [Fact]
    public async Task Izin_listesindeki_host_icin_baglanti_kurulur()
    {
        using var listener = new LocalListener();
        using var client = SafeClient("127.0.0.1");

        using var response = await client.GetAsync($"http://127.0.0.1:{listener.Port}/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, listener.RequestCount);
    }

    [Fact]
    public async Task Izin_listesi_tam_eslesme_ister_baska_isimlere_gecmez()
    {
        using var listener = new LocalListener();
        using var client = SafeClient("127.0.0.1");

        // "localhost" listede değil; aynı IP'ye çözülse de engellenir
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync($"http://localhost:{listener.Port}/"));

        Assert.IsType<UnsafeTargetException>(ex.InnerException);
        Assert.Equal(0, listener.RequestCount);
    }

    [Fact]
    public async Task Yonlendirme_izlenmez_ic_agdaki_ikinci_adrese_sicranmaz()
    {
        using var listener = new LocalListener(_ => (302, "http://169.254.169.254/latest/meta-data/"));
        using var client = SafeClient("127.0.0.1");

        using var response = await client.GetAsync($"http://127.0.0.1:{listener.Port}/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, listener.RequestCount); // ikinci istek hiç atılmadı
    }

    // ----- Webhook kanalı, gerçek dinleyiciyle uçtan uca -----

    private static WebhookChannel CreateChannel(HttpClient client, WebhookSecretProtector protector, params string[] allowed) =>
        new(new StaticHttpClientFactory(client), protector, TimeProvider.System,
            Options.Create(new NotificationOptions { AllowedPrivateHosts = allowed }));

    private static NotificationContext Context(WebhookSecretProtector protector, string secret, string url, Guid eventId) =>
        new(1, "Uçtan Uca Proje",
            new AlertRaised(1, "LOW_SCORE", "Kalite skoru 40.", "Medium", DateTime.UtcNow, eventId),
            new ProjectAutomationSettings
            {
                ProjectId = 1, WebhookUrl = url, WebhookSecretProtected = protector.Protect(secret)
            });

    [Fact]
    public async Task Alici_imzayi_kendi_sirriyla_dogrulayabilir()
    {
        const string secret = "whsec_uctan_uca_test";
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        using var listener = new LocalListener();
        using var client = SafeClient("127.0.0.1");
        var eventId = Guid.NewGuid();

        var result = await CreateChannel(client, protector, "127.0.0.1")
            .SendAsync(Context(protector, secret, $"http://127.0.0.1:{listener.Port}/hooks/alerts", eventId), CancellationToken.None);

        Assert.True(result.Success);
        var request = Assert.Single(listener.Requests);
        Assert.Equal("/hooks/alerts", request.Path);
        Assert.Equal(eventId.ToString(), request.Headers["X-Event-Id"]);

        // Alıcının yapacağı doğrulama: sır + zaman damgası + ham gövde → aynı imza
        var expected = WebhookSigner.HeaderValue(secret, long.Parse(request.Headers["X-Timestamp"]), request.Body);
        Assert.Equal(expected, request.Headers["X-Signature"]);

        // Yanlış sırla aynı imza çıkmaz
        var forged = WebhookSigner.HeaderValue("whsec_baska", long.Parse(request.Headers["X-Timestamp"]), request.Body);
        Assert.NotEqual(forged, request.Headers["X-Signature"]);
    }

    [Fact]
    public async Task Alici_5xx_donerse_gecici_hata_olarak_bildirilir()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        using var listener = new LocalListener(_ => (503, null));
        using var client = SafeClient("127.0.0.1");

        var result = await CreateChannel(client, protector, "127.0.0.1")
            .SendAsync(Context(protector, "whsec_x", $"http://127.0.0.1:{listener.Port}/h", Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Equal(503, result.StatusCode);
    }

    [Fact]
    public async Task Alici_yonlendirirse_basarisiz_sayilir_ve_yonlendirme_izlenmez()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        using var listener = new LocalListener(_ => (302, "http://169.254.169.254/"));
        using var client = SafeClient("127.0.0.1");

        var result = await CreateChannel(client, protector, "127.0.0.1")
            .SendAsync(Context(protector, "whsec_x", $"http://127.0.0.1:{listener.Port}/h", Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(302, result.StatusCode);
        Assert.Equal(1, listener.RequestCount);
    }

    [Fact]
    public async Task Izin_listesi_olmadan_yerel_alici_icin_gonderim_kalici_hata_olur_ve_istek_gitmez()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        using var listener = new LocalListener();
        using var client = SafeClient(); // izin listesi boş

        var result = await CreateChannel(client, protector)
            .SendAsync(Context(protector, "whsec_x", $"http://127.0.0.1:{listener.Port}/h", Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsPermanent);
        Assert.Equal(0, listener.RequestCount);
    }

    [Fact]
    public async Task Hedef_sunucu_kapaliysa_gecici_baglanti_hatasi_doner()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());
        int closedPort;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // port artık kapalı
        using var client = SafeClient("127.0.0.1");

        var result = await CreateChannel(client, protector, "127.0.0.1")
            .SendAsync(Context(protector, "whsec_x", $"http://127.0.0.1:{closedPort}/h", Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Null(result.StatusCode);
    }
}
