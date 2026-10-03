using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Webhook ayarları, sır yönetimi, teslimat listesi, sahiplik ve SSRF reddi (HTTP üzerinden).</summary>
public class NotificationApiTests : IClassFixture<ApiFactory>
{
    private const string PublicUrl = "https://8.8.8.8/hooks/alerts"; // IP sabiti: DNS'e ve ağa çıkmaz

    // GET temiz. POST: açıklama yok (-10), hata kodu yok (-15), kimlik doğrulama yok (-20) → skor 55.
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Bildirim testi", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "summary": "Listeler",
                "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } }
              },
              "post": { "responses": { "201": { "description": "oluştu" } } }
            }
          }
        }
        """;

    private readonly ApiFactory _factory;

    public NotificationApiTests(ApiFactory factory) => _factory = factory;

    private async Task<int> UploadProjectAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
    }

    private static Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client, int projectId, string? webhookUrl, int threshold = 60) =>
        client.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = threshold, notifyOnMissingAuth = true, enabled = true, webhookUrl });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ----- Webhook adresi ve sır -----

    [Fact]
    public async Task Webhook_adresi_ilk_kez_verilince_sir_uretilir_ve_bir_kez_gosterilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-first@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await PutSettingsAsync(owner, projectId, PublicUrl);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await ReadAsync(put);
        var secret = body.GetProperty("newWebhookSecret").GetString();
        Assert.StartsWith("whsec_", secret);
        Assert.True(body.GetProperty("webhookSecretConfigured").GetBoolean());
        Assert.Equal(PublicUrl, body.GetProperty("webhookUrl").GetString());

        // Sonraki okumalarda sır bir daha görünmez
        var get = await owner.GetAsync($"/api/automation/{projectId}/settings");
        var text = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret!, text);
        var settings = JsonDocument.Parse(text).RootElement;
        Assert.True(settings.GetProperty("webhookSecretConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("newWebhookSecret").ValueKind);
    }

    [Fact]
    public async Task Sir_veritabaninda_sifreli_saklanir_duz_hali_bulunmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-encrypted@example.com");
        var projectId = await UploadProjectAsync(owner);

        var secret = (await ReadAsync(await PutSettingsAsync(owner, projectId, PublicUrl)))
            .GetProperty("newWebhookSecret").GetString()!;

        using var scope = _factory.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
            .Single(s => s.ProjectId == projectId).WebhookSecretProtected;
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain(secret, stored);
        Assert.NotEqual(secret, stored);
    }

    [Fact]
    public async Task Adres_sonradan_degisince_mevcut_sir_korunur_yeni_sir_uretilmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-keep@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutSettingsAsync(owner, projectId, PublicUrl);

        var second = await ReadAsync(await PutSettingsAsync(owner, projectId, "https://1.1.1.1/other"));

        Assert.Equal(JsonValueKind.Null, second.GetProperty("newWebhookSecret").ValueKind);
        Assert.True(second.GetProperty("webhookSecretConfigured").GetBoolean());
        Assert.Equal("https://1.1.1.1/other", second.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task Adres_bos_gonderilince_webhook_kapanir_ve_sir_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-clear@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutSettingsAsync(owner, projectId, PublicUrl);

        var cleared = await ReadAsync(await PutSettingsAsync(owner, projectId, null));

        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("webhookUrl").ValueKind);
        Assert.False(cleared.GetProperty("webhookSecretConfigured").GetBoolean());
        using var scope = _factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
            .Single(s => s.ProjectId == projectId).WebhookSecretProtected);
    }

    [Fact]
    public async Task Webhook_siz_ayar_guncellemesi_sir_uretmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-nohook@example.com");
        var projectId = await UploadProjectAsync(owner);

        var body = await ReadAsync(await PutSettingsAsync(owner, projectId, null, threshold: 70));

        Assert.False(body.GetProperty("webhookSecretConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("newWebhookSecret").ValueKind);
        Assert.Equal(70, body.GetProperty("scoreThreshold").GetInt32());
    }

    [Fact]
    public async Task Sir_yenileme_yeni_sirri_bir_kez_gosterir_ve_eskisini_gecersiz_kilar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-rotate@example.com");
        var projectId = await UploadProjectAsync(owner);
        var first = (await ReadAsync(await PutSettingsAsync(owner, projectId, PublicUrl)))
            .GetProperty("newWebhookSecret").GetString()!;
        string storedBefore;
        using (var scope = _factory.Services.CreateScope())
            storedBefore = scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
                .Single(s => s.ProjectId == projectId).WebhookSecretProtected!;

        var rotate = await owner.PostAsync($"/api/automation/{projectId}/webhook/secret", null);

        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var second = (await ReadAsync(rotate)).GetProperty("webhookSecret").GetString()!;
        Assert.StartsWith("whsec_", second);
        Assert.NotEqual(first, second);
        using var after = _factory.Services.CreateScope();
        var storedAfter = after.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
            .Single(s => s.ProjectId == projectId).WebhookSecretProtected!;
        Assert.NotEqual(storedBefore, storedAfter);
        Assert.DoesNotContain(second, storedAfter);
    }

    [Fact]
    public async Task Webhook_adresi_olmadan_sir_yenilenemez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-rotate-nourl@example.com");
        var projectId = await UploadProjectAsync(owner);

        var rotate = await owner.PostAsync($"/api/automation/{projectId}/webhook/secret", null);

        Assert.Equal(HttpStatusCode.BadRequest, rotate.StatusCode);
    }

    // ----- SSRF reddi (HTTP hattı üzerinden) -----

    [Theory]
    [InlineData("http://8.8.8.8/hook")]                          // düz http
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://172.16.0.1/hook")]
    [InlineData("https://192.168.1.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]    // bulut metadata
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[::ffff:127.0.0.1]/hook")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("https://2130706433/hook")]
    [InlineData("https://0x7f000001/hook")]
    [InlineData("ftp://8.8.8.8/hook")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@8.8.8.8/hook")]
    [InlineData("bu bir adres degil")]
    public async Task Guvensiz_adresler_400_ile_reddedilir_ve_ayar_degismez(string url)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"notif-ssrf-{Math.Abs(url.GetHashCode())}@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await PutSettingsAsync(owner, projectId, url);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("reddedildi", await put.Content.ReadAsStringAsync());
        var settings = await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/settings"));
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("webhookUrl").ValueKind);
        Assert.False(settings.GetProperty("webhookSecretConfigured").GetBoolean());
    }

    // ----- Sahiplik ve kimlik -----

    [Fact]
    public async Task Baska_kullanici_webhook_ayarlayamaz_sir_yenileyemez_teslimat_goremez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("notif-attacker@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutSettingsAsync(owner, projectId, PublicUrl);

        Assert.Equal(HttpStatusCode.NotFound, (await PutSettingsAsync(attacker, projectId, "https://1.1.1.1/evil")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/automation/{projectId}/webhook/secret", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/deliveries")).StatusCode);

        // Saldırgan adresi değiştiremedi
        var settings = await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/settings"));
        Assert.Equal(PublicUrl, settings.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task Tokensiz_istekler_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-anon@example.com");
        var projectId = await UploadProjectAsync(owner);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/automation/{projectId}/webhook/secret", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutSettingsAsync(anonymous, projectId, PublicUrl)).StatusCode);
    }

    // ----- Teslimatlar -----

    [Fact]
    public async Task Yeni_uyari_webhook_icin_Pending_teslimat_uretir_ve_listelenir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-deliveries@example.com");
        var projectId = await UploadProjectAsync(owner);   // skor 55 → LOW_SCORE açıldı (webhook henüz yok)

        // Eşiği düşür + webhook ekle, yeniden analiz et: LOW_SCORE çözülür
        await PutSettingsAsync(owner, projectId, PublicUrl, threshold: 50);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();
        var none = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries"))).EnumerateArray().ToList();
        Assert.Empty(none);

        // Eşiği geri yükselt: LOW_SCORE yeniden tutar → yeni uyarı → teslimat
        await PutSettingsAsync(owner, projectId, PublicUrl, threshold: 60);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();

        var deliveries = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries"))).EnumerateArray().ToList();
        var delivery = Assert.Single(deliveries);
        Assert.Equal("webhook", delivery.GetProperty("channel").GetString());
        Assert.Equal("Pending", delivery.GetProperty("status").GetString());
        Assert.Equal(0, delivery.GetProperty("attempts").GetInt32());
        Assert.NotEqual(Guid.Empty, delivery.GetProperty("eventId").GetGuid());

        var pending = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries?status=Pending"))).EnumerateArray().ToList();
        Assert.Single(pending);
        var dead = (await ReadAsync(await owner.GetAsync($"/api/automation/{projectId}/deliveries?status=Dead"))).EnumerateArray().ToList();
        Assert.Empty(dead);
    }

    [Fact]
    public async Task Gecersiz_teslimat_durumu_filtresi_400_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-badstatus@example.com");
        var projectId = await UploadProjectAsync(owner);

        var response = await owner.GetAsync($"/api/automation/{projectId}/deliveries?status=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Proje_silinince_teslimatlar_da_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("notif-delete@example.com");
        var projectId = await UploadProjectAsync(owner);
        await PutSettingsAsync(owner, projectId, PublicUrl, threshold: 50);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();
        await PutSettingsAsync(owner, projectId, PublicUrl, threshold: 60);
        (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).EnsureSuccessStatusCode();

        using (var before = _factory.Services.CreateScope())
            Assert.NotEmpty(before.ServiceProvider.GetRequiredService<AppDbContext>()
                .NotificationDeliveries.Where(d => d.ProjectId == projectId));

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);

        using var after = _factory.Services.CreateScope();
        Assert.Empty(after.ServiceProvider.GetRequiredService<AppDbContext>()
            .NotificationDeliveries.Where(d => d.ProjectId == projectId));
    }
}
