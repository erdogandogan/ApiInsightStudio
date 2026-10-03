using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Otomasyon uç noktaları: ayarlar, uyarılar, sahiplik ve proje silinince temizlik.</summary>
public class AutomationApiTests : IClassFixture<ApiFactory>
{
    // GET: temiz. POST: açıklama yok (-10), hata kodu yok (-15), kimlik doğrulama yok (-20) → skor 55.
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Otomasyon testi", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "summary": "Listeler",
                "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } }
              },
              "post": {
                "responses": { "201": { "description": "oluştu" } }
              }
            }
          }
        }
        """;

    private readonly ApiFactory _factory;

    public AutomationApiTests(ApiFactory factory) => _factory = factory;

    private async Task<int> UploadProjectAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<JsonElement>(url);

    [Fact]
    public async Task Ayar_kaydi_yokken_varsayilanlar_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-defaults@example.com");
        var projectId = await UploadProjectAsync(owner);

        var settings = await GetJsonAsync(owner, $"/api/automation/{projectId}/settings");

        Assert.Equal(60, settings.GetProperty("scoreThreshold").GetInt32());
        Assert.True(settings.GetProperty("notifyOnMissingAuth").GetBoolean());
        Assert.True(settings.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Yukleme_uyarilari_otomatik_acar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-upload@example.com");
        var projectId = await UploadProjectAsync(owner);

        var alerts = await GetJsonAsync(owner, $"/api/automation/{projectId}/alerts");

        var codes = alerts.EnumerateArray().Select(a => a.GetProperty("ruleCode").GetString()).ToList();
        Assert.Contains("LOW_SCORE", codes);   // skor 55 < 60
        Assert.Contains("MISSING_AUTH", codes);
        Assert.All(alerts.EnumerateArray(), a => Assert.Equal("Open", a.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Esik_dusurulup_yeniden_analiz_edilince_LOW_SCORE_cozulur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-threshold@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = 50, notifyOnMissingAuth = true, enabled = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).StatusCode);

        var alerts = (await GetJsonAsync(owner, $"/api/automation/{projectId}/alerts")).EnumerateArray().ToList();
        Assert.Equal("Resolved", alerts.Single(a => a.GetProperty("ruleCode").GetString() == "LOW_SCORE").GetProperty("status").GetString());
        Assert.Equal("Open", alerts.Single(a => a.GetProperty("ruleCode").GetString() == "MISSING_AUTH").GetProperty("status").GetString());

        var open = (await GetJsonAsync(owner, $"/api/automation/{projectId}/alerts?status=Open")).EnumerateArray().ToList();
        Assert.Single(open);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Gecersiz_esik_400_doner_ve_ayar_degismez(int threshold)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"auto-invalid{threshold + 5}@example.com");
        var projectId = await UploadProjectAsync(owner);

        var put = await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = threshold, notifyOnMissingAuth = true, enabled = true });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var settings = await GetJsonAsync(owner, $"/api/automation/{projectId}/settings");
        Assert.Equal(60, settings.GetProperty("scoreThreshold").GetInt32());
    }

    [Fact]
    public async Task Gecersiz_status_filtresi_400_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-status@example.com");
        var projectId = await UploadProjectAsync(owner);

        var response = await owner.GetAsync($"/api/automation/{projectId}/alerts?status=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Baska_kullanici_ayar_ve_uyarilara_erisemez_404()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("auto-attacker@example.com");
        var projectId = await UploadProjectAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/alerts")).StatusCode);
        var put = await attacker.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = 0, notifyOnMissingAuth = false, enabled = false });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        // Saldırgan hiçbir şeyi değiştiremedi
        var settings = await GetJsonAsync(owner, $"/api/automation/{projectId}/settings");
        Assert.Equal(60, settings.GetProperty("scoreThreshold").GetInt32());
        Assert.True(settings.GetProperty("enabled").GetBoolean());
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings.Where(s => s.ProjectId == projectId));
    }

    [Fact]
    public async Task Tokensiz_istekler_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-anon@example.com");
        var projectId = await UploadProjectAsync(owner);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/alerts")).StatusCode);
        var put = await anonymous.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = 10, notifyOnMissingAuth = true, enabled = true });
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }

    [Fact]
    public async Task Proje_silinince_ayar_ve_uyarilar_da_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("auto-delete@example.com");
        var projectId = await UploadProjectAsync(owner);
        (await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings",
            new { scoreThreshold = 70, notifyOnMissingAuth = true, enabled = true })).EnsureSuccessStatusCode();

        using (var before = _factory.Services.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEmpty(db.Alerts.Where(a => a.ProjectId == projectId));
            Assert.NotEmpty(db.ProjectAutomationSettings.Where(s => s.ProjectId == projectId));
        }

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);

        using var after = _factory.Services.CreateScope();
        var dbAfter = after.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(dbAfter.Alerts.Where(a => a.ProjectId == projectId));
        Assert.Empty(dbAfter.ProjectAutomationSettings.Where(s => s.ProjectId == projectId));
    }
}
