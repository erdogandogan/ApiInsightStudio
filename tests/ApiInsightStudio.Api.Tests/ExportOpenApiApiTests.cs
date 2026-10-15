using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApiInsightStudio.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Onaylı AI açıklamalarıyla güncellenmiş OpenAPI kopyasını indirme: HTTP hattı üzerinden.</summary>
public class ExportOpenApiApiTests : IClassFixture<ApiFactory>
{
    // GET: açıklaması VAR ("Listeler"). POST: açıklama yok → AI açıklaması yazılabilir.
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Dışa aktarma testi", "version": "1.0.0" },
          "paths": {
            "/items": {
              "get": {
                "summary": "Listeler",
                "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } }
              },
              "post": {
                "security": [ { "bearerAuth": [] } ],
                "responses": { "201": { "description": "oluştu" }, "400": { "description": "hatalı" } }
              }
            }
          },
          "components": { "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } } }
        }
        """;

    private readonly ApiFactory _factory;

    public ExportOpenApiApiTests(ApiFactory factory) => _factory = factory;

    private async Task<(int ProjectId, int PostEndpointId, int GetEndpointId)> UploadAsync(HttpClient owner, string? name = null)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument, name });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var projectId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var endpoints = db.Endpoints.Where(e => e.ProjectId == projectId).ToList();
        return (projectId, endpoints.Single(e => e.Method == "POST").Id, endpoints.Single(e => e.Method == "GET").Id);
    }

    private static async Task<int> GenerateAsync(HttpClient client, int projectId, int endpointId)
    {
        var response = await client.PostAsync($"/api/project/{projectId}/endpoint/{endpointId}/generate-ai-description", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("suggestionId").GetInt32();
    }

    private static async Task ApproveAsync(HttpClient client, int projectId, int suggestionId, string? edited = null)
    {
        var response = await client.PostAsJsonAsync($"/api/automation/{projectId}/ai-suggestions/{suggestionId}/approve",
            edited is null ? new { } : (object)new { editedContent = edited });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> ExportAsync(HttpClient client, int projectId) =>
        client.GetAsync($"/api/project/{projectId}/export-openapi");

    private static int FilledCount(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("X-Filled-Count").Single());

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private T WithDb<T>(Func<AppDbContext, T> read)
    {
        using var scope = _factory.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Onayli_aciklama_indirilen_kopyada_summary_olur_ve_dosya_olarak_gelir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-basic@example.com");
        var (projectId, postId, _) = await UploadAsync(owner, name: "Dışa aktarma projesi");
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId));

        var response = await ExportAsync(owner, projectId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, FilledCount(response));
        Assert.Contains("guncellenmis-openapi.json", response.Content.Headers.ContentDisposition?.ToString() ?? "");

        var doc = await BodyAsync(response);
        Assert.Equal(ApiFactory.OllamaReplyText, (string?)doc["paths"]!["/items"]!["post"]!["summary"]);
        Assert.True((bool)doc["paths"]!["/items"]!["post"]!["x-ai-onayli"]!);
        Assert.Equal("Listeler", (string?)doc["paths"]!["/items"]!["get"]!["summary"]);   // yazarın metni aynen
    }

    [Fact]
    public async Task Duzenlenerek_onaylanan_metin_indirilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-edited@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId), edited: "Yeni ürün kaydı oluşturur.");

        var doc = await BodyAsync(await ExportAsync(owner, projectId));

        Assert.Equal("Yeni ürün kaydı oluşturur.", (string?)doc["paths"]!["/items"]!["post"]!["summary"]);
    }

    [Fact]
    public async Task Bekleyen_ve_reddedilen_oneriler_indirilen_dosyaya_girmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-pending@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        var pending = await GenerateAsync(owner, projectId, postId);

        var whilePending = await ExportAsync(owner, projectId);
        Assert.Equal(0, FilledCount(whilePending));
        Assert.Null((await BodyAsync(whilePending))["paths"]!["/items"]!["post"]!["summary"]);

        await owner.PostAsJsonAsync($"/api/automation/{projectId}/ai-suggestions/{pending}/reject", new { });
        var afterReject = await ExportAsync(owner, projectId);
        Assert.Equal(0, FilledCount(afterReject));
        Assert.Null((await BodyAsync(afterReject))["paths"]!["/items"]!["post"]!["summary"]);
    }

    [Fact]
    public async Task Yazarin_kendi_aciklamasi_uzerine_yazilmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-keep@example.com");
        var (projectId, _, getId) = await UploadAsync(owner);
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, getId));   // GET'in zaten "Listeler" açıklaması var

        var response = await ExportAsync(owner, projectId);

        Assert.Equal(0, FilledCount(response));
        Assert.Equal("Listeler", (string?)(await BodyAsync(response))["paths"]!["/items"]!["get"]!["summary"]);
    }

    [Fact]
    public async Task Yeni_onay_indirilen_metni_gunceller()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-reapprove@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId), edited: "Birinci metin.");
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId), edited: "İkinci metin.");

        var doc = await BodyAsync(await ExportAsync(owner, projectId));

        Assert.Equal("İkinci metin.", (string?)doc["paths"]!["/items"]!["post"]!["summary"]);
    }

    [Fact]
    public async Task Projenin_kendisi_skoru_ve_uyarilari_degismez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-untouched@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId));
        var before = await owner.GetFromJsonAsync<JsonElement>($"/api/project/{projectId}/dashboard");
        var storedBefore = WithDb(db => db.Projects.AsNoTracking().Single(p => p.Id == projectId).OpenApiContent);

        await ExportAsync(owner, projectId);

        var after = await owner.GetFromJsonAsync<JsonElement>($"/api/project/{projectId}/dashboard");
        Assert.Equal(before.GetProperty("kaliteSkoru").GetInt32(), after.GetProperty("kaliteSkoru").GetInt32());
        Assert.Equal(before.GetProperty("warnings").GetArrayLength(), after.GetProperty("warnings").GetArrayLength());
        Assert.Equal(storedBefore, WithDb(db => db.Projects.AsNoTracking().Single(p => p.Id == projectId).OpenApiContent));
        Assert.Equal(string.Empty, WithDb(db => db.Endpoints.AsNoTracking().Single(e => e.Id == postId).Summary));
    }

    [Fact]
    public async Task Indirilen_dosya_yeniden_yuklenince_acik_kapanir_ve_skor_artar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-roundtrip@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        var original = await owner.GetFromJsonAsync<JsonElement>($"/api/project/{projectId}/dashboard");
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId));
        var exported = await (await ExportAsync(owner, projectId)).Content.ReadAsStringAsync();

        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = exported });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var newId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
        var updated = await owner.GetFromJsonAsync<JsonElement>($"/api/project/{newId}/dashboard");

        Assert.Equal(original.GetProperty("kaliteSkoru").GetInt32() + 10, updated.GetProperty("kaliteSkoru").GetInt32());   // açıklama eksik: -10 kalktı
        Assert.DoesNotContain(updated.GetProperty("warnings").EnumerateArray(),
            w => w.GetProperty("message").GetString() == "Endpoint açıklaması eksik");
    }

    [Fact]
    public async Task Baska_kullanici_404_anonim_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("export-attacker@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId));

        var other = await ExportAsync(attacker, projectId);
        var anonymous = await ExportAsync(_factory.CreateClient(), projectId);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.DoesNotContain(ApiFactory.OllamaReplyText, await other.Content.ReadAsStringAsync());   // içerik sızmaz
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ExportAsync(owner, 999999)).StatusCode);
    }

    [Fact]
    public async Task Indirme_denetim_izine_yalnizca_sayilarla_yazilir_bos_indirme_yazilmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-audit@example.com");
        var (projectId, postId, _) = await UploadAsync(owner);
        await ExportAsync(owner, projectId);   // hiç onaylı açıklama yok → kayıt yok
        Assert.DoesNotContain("openapi.exported", await owner.GetStringAsync($"/api/automation/{projectId}/audit?take=100"));

        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId), edited: "GIZLI-ACIKLAMA-METNI");
        await ExportAsync(owner, projectId);

        var raw = await owner.GetStringAsync($"/api/automation/{projectId}/audit?take=100");
        var entry = JsonNode.Parse(raw)!["items"]!.AsArray().Single(i => (string?)i!["action"] == "openapi.exported")!;
        Assert.Equal("filled=1; skippedExisting=0; notInDocument=0", (string?)entry["details"]);
        Assert.NotNull((int?)entry["actorUserId"]);
        Assert.DoesNotContain("GIZLI-ACIKLAMA-METNI", raw);   // açıklama metni denetim izine yazılmaz
        Assert.True((await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/audit/verify")).GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Dosya_adi_guvenlidir_ve_basliga_ozel_karakter_sizmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-filename@example.com");
        var (projectId, postId, _) = await UploadAsync(owner, name: "../Etc/\"Proje\"\r\nX-Evil: 1");
        await ApproveAsync(owner, projectId, await GenerateAsync(owner, projectId, postId));

        var response = await ExportAsync(owner, projectId);

        var disposition = response.Content.Headers.ContentDisposition!;
        var fileName = disposition.FileNameStar ?? disposition.FileName ?? "";
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain("\\", fileName);
        Assert.DoesNotContain("\"", fileName.Trim('"'));
        Assert.DoesNotContain("..", fileName);
        Assert.False(response.Headers.Contains("X-Evil"));
        Assert.EndsWith("-guncellenmis-openapi.json", fileName.Trim('"'));
    }

    [Fact]
    public async Task Tarayicinin_okuyabilmesi_icin_basliklar_CORS_ile_acilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("export-cors@example.com");
        var (projectId, _, _) = await UploadAsync(owner);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/project/{projectId}/export-openapi");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Authorization = owner.DefaultRequestHeaders.Authorization;

        var response = await _factory.CreateClient().SendAsync(request);

        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("X-Filled-Count", exposed);
        Assert.Contains("Content-Disposition", exposed);
    }
}
