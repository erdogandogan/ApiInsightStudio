using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Başka kullanıcının projesine erişimin (IDOR/BOLA) kapalı olduğunu gerçek HTTP hattı üzerinden doğrular.
/// Sahip istekleri 200, başka kullanıcı 404 (varlık sızdırılmaz), token'sız istek 401 almalı.
/// </summary>
public class OwnershipIntegrationTests : IClassFixture<ApiFactory>
{
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Sahiplik testi", "version": "1.0.0" },
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

    public OwnershipIntegrationTests(ApiFactory factory) => _factory = factory;

    private async Task<(int ProjectId, int EndpointId)> UploadProjectAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var projectId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var endpointId = db.Endpoints.Where(e => e.ProjectId == projectId && e.Method == "POST").Select(e => e.Id).Single();
        return (projectId, endpointId);
    }

    [Fact]
    public async Task Sahip_kendi_projesine_erisir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("owner-a@example.com");
        var (projectId, endpointId) = await UploadProjectAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/project/{projectId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/analysis/{projectId}/analyze", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsync($"/api/project/{projectId}/endpoint/{endpointId}/generate-ai-description", null)).StatusCode);
    }

    [Fact]
    public async Task Baska_kullanici_ayni_isteklerde_404_alir_ve_hicbir_sey_degismez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("owner-b@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("attacker-b@example.com");
        var (projectId, endpointId) = await UploadProjectAsync(owner);
        var callsBefore = _factory.OllamaCalls;

        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/project/{projectId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/analysis/{projectId}/analyze", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await attacker.PostAsync($"/api/project/{projectId}/endpoint/{endpointId}/generate-ai-description", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.DeleteAsync($"/api/project/{projectId}")).StatusCode);

        // Saldırgan modele ulaşamadı, proje silinmedi, AI açıklaması yazılmadı
        Assert.Equal(callsBefore, _factory.OllamaCalls);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/project/{projectId}/dashboard")).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db.Endpoints.Single(e => e.Id == endpointId).AiSummary);
    }

    [Fact]
    public async Task Baskasinin_projesi_kullanicinin_proje_listesinde_gorunmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("owner-c@example.com");
        var other = await _factory.CreateAuthenticatedClientAsync("other-c@example.com");
        var (projectId, _) = await UploadProjectAsync(owner);

        var list = await other.GetFromJsonAsync<JsonElement>("/api/project");

        Assert.DoesNotContain(list.EnumerateArray(), p => p.GetProperty("id").GetInt32() == projectId);
    }

    [Fact]
    public async Task Tokensiz_istekler_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("owner-d@example.com");
        var (projectId, endpointId) = await UploadProjectAsync(owner);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/project/{projectId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/analysis/{projectId}/analyze", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"/api/project/{projectId}/endpoint/{endpointId}/generate-ai-description", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/project/{projectId}")).StatusCode);
    }

    [Fact]
    public async Task Sahip_projeyi_silebilir_sonra_proje_404_verir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("owner-e@example.com");
        var (projectId, _) = await UploadProjectAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/project/{projectId}/dashboard")).StatusCode);
    }
}
