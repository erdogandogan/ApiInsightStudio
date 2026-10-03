using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.TestRunner;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Test koşusu uç noktaları: ayarlar, hedef token'ı, kuyruğa alma, sonuçlar, sahiplik ve temizlik.</summary>
public class TestRunnerApiTests : IClassFixture<RunnerApiFactory>
{
    private const string Target = "https://8.8.8.8"; // IP sabiti: DNS'e ve ağa çıkmaz
    private const string TargetToken = "tok.GIZLI-123_abc";

    // 11 senaryo üretir: GET /items (açık), POST /items, GET /items/{id}, DELETE /items/{id} (Bearer ister)
    private const string TypicalDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Koşucu API testi", "version": "1.0.0" },
          "components": { "securitySchemes": { "bearerAuth": { "type": "http", "scheme": "bearer" } } },
          "paths": {
            "/items": {
              "get": { "summary": "Listeler",
                "responses": { "200": { "description": "ok" }, "400": { "description": "hatalı" } } },
              "post": { "summary": "Oluşturur", "security": [ { "bearerAuth": [] } ],
                "responses": { "201": { "description": "ok" }, "400": { "description": "hatalı" } } }
            },
            "/items/{id}": {
              "get": { "summary": "Getirir", "security": [ { "bearerAuth": [] } ],
                "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "ok" }, "404": { "description": "yok" } } },
              "delete": { "summary": "Siler", "security": [ { "bearerAuth": [] } ],
                "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "integer" } } ],
                "responses": { "200": { "description": "ok" }, "404": { "description": "yok" } } }
            }
          }
        }
        """;

    private const string EmptyDocument = """
        { "openapi": "3.0.0", "info": { "title": "Boş API", "version": "1.0.0" }, "paths": {} }
        """;

    private readonly RunnerApiFactory _factory;

    public TestRunnerApiTests(RunnerApiFactory factory) => _factory = factory;

    private async Task<int> UploadAsync(HttpClient owner, string document = TypicalDocument)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = document });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        return (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
    }

    private static Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client, int projectId, string? target, bool allowMutating = false, int threshold = 20) =>
        client.PutAsJsonAsync($"/api/automation/{projectId}/settings", new
        {
            scoreThreshold = 60, notifyOnMissingAuth = true, enabled = true, notifyTelegram = false,
            webhookUrl = (string?)null, targetBaseUrl = target, allowMutatingTests = allowMutating,
            testFailureThresholdPercent = threshold
        });

    private static Task<HttpResponseMessage> PutTokenAsync(HttpClient client, int projectId, string? token) =>
        client.PutAsJsonAsync($"/api/automation/{projectId}/target-token", new { token });

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> GetSettingsAsync(HttpClient client, int projectId) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/settings");

    /// <summary>Kuyruk global (FIFO) olduğundan, başka testlerden kalan koşular dahil kuyruk boşalana kadar işler.</summary>
    private async Task ProcessQueueAsync()
    {
        for (var i = 0; i < 25; i++)
        {
            using var scope = _factory.Services.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<TestRunProcessor>().ProcessPendingAsync() == 0)
                break;
        }
    }

    /// <summary>Kuyrukta bekleyen koşu bırakan testler, sonraki testleri etkilemesin diye projelerini siler (koşular cascade ile gider).</summary>
    private static async Task CleanUpAsync(HttpClient owner, params int[] projectIds)
    {
        foreach (var projectId in projectIds)
            Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);
    }

    private async Task<JsonElement> RunToCompletionAsync(HttpClient owner, int projectId)
    {
        var start = await owner.PostAsync($"/api/automation/{projectId}/test-runs", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var runId = (await ReadAsync(start)).GetProperty("id").GetInt32();
        await ProcessQueueAsync();
        return await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/test-runs/{runId}");
    }

    // ----- Ayarlar -----

    [Fact]
    public async Task Varsayilan_ayarlar_test_koşusu_icin_guvenlidir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-defaults@example.com");
        var projectId = await UploadAsync(owner);

        var settings = await GetSettingsAsync(owner, projectId);

        Assert.Equal(JsonValueKind.Null, settings.GetProperty("targetBaseUrl").ValueKind);
        Assert.False(settings.GetProperty("allowMutatingTests").GetBoolean());
        Assert.Equal(20, settings.GetProperty("testFailureThresholdPercent").GetInt32());
        Assert.False(settings.GetProperty("targetTokenConfigured").GetBoolean());
    }

    [Fact]
    public async Task Gecerli_hedef_ve_ayarlar_kaydedilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-save@example.com");
        var projectId = await UploadAsync(owner);

        var put = await PutSettingsAsync(owner, projectId, "https://8.8.8.8/api", allowMutating: true, threshold: 35);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var settings = await GetSettingsAsync(owner, projectId);
        Assert.Equal("https://8.8.8.8/api", settings.GetProperty("targetBaseUrl").GetString());
        Assert.True(settings.GetProperty("allowMutatingTests").GetBoolean());
        Assert.Equal(35, settings.GetProperty("testFailureThresholdPercent").GetInt32());
    }

    [Theory]
    [InlineData("http://8.8.8.8")]                  // düz http
    [InlineData("https://127.0.0.1")]
    [InlineData("https://localhost")]
    [InlineData("https://10.0.0.1/api")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://[::1]/api")]
    [InlineData("https://2130706433")]
    [InlineData("https://user:pw@8.8.8.8")]
    [InlineData("https://8.8.8.8/api?key=1")]       // sorgu
    [InlineData("https://8.8.8.8/api#bolum")]       // parça
    [InlineData("ftp://8.8.8.8")]
    [InlineData("file:///etc/passwd")]
    [InlineData("bu bir adres degil")]
    public async Task Guvensiz_hedef_adresler_400_ile_reddedilir_ve_ayar_degismez(string target)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"runner-ssrf-{Math.Abs(target.GetHashCode())}@example.com");
        var projectId = await UploadAsync(owner);

        var put = await PutSettingsAsync(owner, projectId, target);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Hedef adres reddedildi", await put.Content.ReadAsStringAsync());
        var settings = await GetSettingsAsync(owner, projectId);
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("targetBaseUrl").ValueKind);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Gecersiz_basarisizlik_esigi_400_doner(int threshold)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"runner-threshold{threshold + 5}@example.com");
        var projectId = await UploadAsync(owner);

        var put = await PutSettingsAsync(owner, projectId, Target, threshold: threshold);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    // ----- Hedef token'ı -----

    [Fact]
    public async Task Token_hedef_adres_olmadan_tanimlanamaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-nourl@example.com");
        var projectId = await UploadAsync(owner);

        var response = await PutTokenAsync(owner, projectId, TargetToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_tanimlanir_ama_hicbir_cevapta_geri_donmez_ve_veritabaninda_sifrelidir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-set@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);

        var put = await PutTokenAsync(owner, projectId, TargetToken);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var putText = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TargetToken, putText);
        Assert.True(JsonDocument.Parse(putText).RootElement.GetProperty("targetTokenConfigured").GetBoolean());

        var settingsText = await (await owner.GetAsync($"/api/automation/{projectId}/settings")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(TargetToken, settingsText);
        Assert.True(JsonDocument.Parse(settingsText).RootElement.GetProperty("targetTokenConfigured").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var stored = scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
            .Single(s => s.ProjectId == projectId).TargetBearerTokenProtected;
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain(TargetToken, stored);
    }

    [Theory]
    [InlineData("iki kelime")]            // boşluk
    [InlineData("tok\nen")]               // satır sonu: üstbilgi enjeksiyonu
    [InlineData("tok\ren")]
    [InlineData("tök")]                   // ASCII dışı
    [InlineData("tok\u0007en")]           // denetim karakteri
    [InlineData("")]
    [InlineData("   ")]
    public async Task Gecersiz_token_karakterleri_400_ile_reddedilir(string token)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"runner-token-bad{Math.Abs(token.GetHashCode())}@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);

        var put = await PutTokenAsync(owner, projectId, token);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.False((await GetSettingsAsync(owner, projectId)).GetProperty("targetTokenConfigured").GetBoolean());
    }

    [Fact]
    public async Task Cok_uzun_token_reddedilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-long@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);

        var put = await PutTokenAsync(owner, projectId, new string('a', 4097));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Token_silinebilir_ve_silme_tekrarlanabilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-del@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);

        var first = await owner.DeleteAsync($"/api/automation/{projectId}/target-token");
        var second = await owner.DeleteAsync($"/api/automation/{projectId}/target-token");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False((await GetSettingsAsync(owner, projectId)).GetProperty("targetTokenConfigured").GetBoolean());
    }

    [Fact]
    public async Task Ayni_kokenli_adres_degisikliginde_token_korunur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-keep@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, "https://8.8.8.8/v1");
        await PutTokenAsync(owner, projectId, TargetToken);

        await PutSettingsAsync(owner, projectId, "https://8.8.8.8/v2"); // yalnızca yol değişti

        Assert.True((await GetSettingsAsync(owner, projectId)).GetProperty("targetTokenConfigured").GetBoolean());
    }

    [Theory]
    [InlineData("https://1.1.1.1/v1")]      // ana bilgisayar değişti
    [InlineData("https://8.8.8.8:8443/v1")] // port değişti
    public async Task Adresin_koken_degisince_token_silinir_yeni_adrese_sizmaz(string newTarget)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"runner-token-move-{newTarget.GetHashCode() & 0xFFFF}@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, "https://8.8.8.8/v1");
        await PutTokenAsync(owner, projectId, TargetToken);

        await PutSettingsAsync(owner, projectId, newTarget);

        Assert.False((await GetSettingsAsync(owner, projectId)).GetProperty("targetTokenConfigured").GetBoolean());
        using var scope = _factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetRequiredService<AppDbContext>().ProjectAutomationSettings
            .Single(s => s.ProjectId == projectId).TargetBearerTokenProtected);
    }

    [Fact]
    public async Task Hedef_adres_silinince_token_da_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-token-clear@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);

        await PutSettingsAsync(owner, projectId, null);

        var settings = await GetSettingsAsync(owner, projectId);
        Assert.Equal(JsonValueKind.Null, settings.GetProperty("targetBaseUrl").ValueKind);
        Assert.False(settings.GetProperty("targetTokenConfigured").GetBoolean());
    }

    // ----- Kuyruğa alma -----

    [Fact]
    public async Task Hedef_adres_yoksa_koşu_baslatilamaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-start-nourl@example.com");
        var projectId = await UploadAsync(owner);

        var response = await owner.PostAsync($"/api/automation/{projectId}/test-runs", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("targetBaseUrl", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Senaryo_yoksa_koşu_baslatilamaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-start-noscenario@example.com");
        var projectId = await UploadAsync(owner, EmptyDocument);
        await PutSettingsAsync(owner, projectId, Target);

        var response = await owner.PostAsync($"/api/automation/{projectId}/test-runs", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("senaryosu yok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Koşu_202_ile_kuyruga_alinir_Location_basligi_verir_ve_ikincisi_409_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-start@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);

        var first = await owner.PostAsync($"/api/automation/{projectId}/test-runs", null);
        var second = await owner.PostAsync($"/api/automation/{projectId}/test-runs", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var body = await ReadAsync(first);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal($"/api/automation/{projectId}/test-runs/{body.GetProperty("id").GetInt32()}",
            first.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var list = (await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/test-runs")).EnumerateArray().ToList();
        Assert.Single(list); // ikinci istek kayıt oluşturmadı

        await CleanUpAsync(owner, projectId);
    }

    [Fact]
    public async Task Bekleyen_koşunun_ayrintisi_bos_sonuc_listesiyle_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-pending@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        var runId = (await ReadAsync(await owner.PostAsync($"/api/automation/{projectId}/test-runs", null))).GetProperty("id").GetInt32();

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/test-runs/{runId}");

        Assert.Equal("Pending", detail.GetProperty("status").GetString());
        Assert.Empty(detail.GetProperty("results").EnumerateArray());

        await CleanUpAsync(owner, projectId);
    }

    // ----- Uçtan uca: kuyruk → işleyici → sonuçlar -----

    [Fact]
    public async Task Koşu_islenir_sonuclar_okunur_ve_ardindan_yeni_koşu_baslatilabilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-e2e@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);
        var before = _factory.TargetRequests.Count;

        var run = await RunToCompletionAsync(owner, projectId);

        Assert.Equal("Completed", run.GetProperty("status").GetString());
        Assert.Equal("8.8.8.8", run.GetProperty("targetHost").GetString());
        Assert.Equal(11, run.GetProperty("total").GetInt32());
        Assert.Equal(3, run.GetProperty("passed").GetInt32());
        Assert.Equal(0, run.GetProperty("failed").GetInt32());
        Assert.Equal(8, run.GetProperty("skipped").GetInt32());

        var results = run.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(11, results.Count);
        var notFound = results.Single(r => r.GetProperty("method").GetString() == "GET"
            && r.GetProperty("path").GetString() == "/items/{id}" && r.GetProperty("expectedStatusCode").GetInt32() == 404);
        Assert.Equal("Passed", notFound.GetProperty("outcome").GetString());
        Assert.Equal("/items/0", notFound.GetProperty("requestPath").GetString());
        Assert.Equal(404, notFound.GetProperty("actualStatusCode").GetInt32());

        // Hedefe yalnızca 3 GET gitti (mutating kapalı)
        var sent = _factory.TargetRequests.Skip(before).ToList();
        Assert.Equal(3, sent.Count);
        Assert.All(sent, r => Assert.Equal("GET", r.Method));

        // Token cevapta görünmez
        Assert.DoesNotContain(TargetToken, run.GetRawText());

        // Biten koşudan sonra yeni koşu başlatılabilir (409 yok)
        Assert.Equal(HttpStatusCode.Accepted, (await owner.PostAsync($"/api/automation/{projectId}/test-runs", null)).StatusCode);

        await CleanUpAsync(owner, projectId); // bu son koşu kuyrukta kalıp sonraki testleri etkilemesin
    }

    [Fact]
    public async Task Mutating_ayari_ayarlar_uzerinden_acilinca_yalnizca_olumsuz_mutating_senaryolar_calisir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-mutating@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target, allowMutating: true);
        await PutTokenAsync(owner, projectId, TargetToken);
        var before = _factory.TargetRequests.Count;

        var run = await RunToCompletionAsync(owner, projectId);

        Assert.Equal(7, run.GetProperty("passed").GetInt32());
        Assert.Equal(4, run.GetProperty("skipped").GetInt32());
        var sent = _factory.TargetRequests.Skip(before).ToList();
        Assert.Equal(7, sent.Count);
        Assert.Equal(2, sent.Count(r => r.Method == "POST"));
        Assert.Equal(2, sent.Count(r => r.Method == "DELETE"));
    }

    [Fact]
    public async Task Token_tanimli_degilse_kimlik_isteyen_senaryolar_atlanir_ve_hedefe_token_gitmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-notoken@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        var before = _factory.TargetRequests.Count;

        var run = await RunToCompletionAsync(owner, projectId);

        Assert.All(_factory.TargetRequests.Skip(before), r => Assert.Null(r.Authorization));
        var reasons = run.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("reason").GetString()).Where(r => r is not null).ToList();
        Assert.Contains(reasons, r => r!.Contains("token"));
    }

    [Fact]
    public async Task Basarisiz_koşu_TEST_FAILURES_uyarisi_acar_duzelince_cozer()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-alert@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);

        _factory.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        try
        {
            var failing = await RunToCompletionAsync(owner, projectId);
            Assert.Equal(3, failing.GetProperty("failed").GetInt32());
        }
        finally
        {
            _factory.Respond = FakeTargetHandler.TypicalApi;
        }

        var open = (await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/alerts?status=Open"))
            .EnumerateArray().ToList();
        Assert.Contains(open, a => a.GetProperty("ruleCode").GetString() == "TEST_FAILURES");

        await RunToCompletionAsync(owner, projectId); // sağlıklı koşu

        var resolved = (await owner.GetFromJsonAsync<JsonElement>($"/api/automation/{projectId}/alerts?status=Resolved"))
            .EnumerateArray().ToList();
        Assert.Contains(resolved, a => a.GetProperty("ruleCode").GetString() == "TEST_FAILURES");
    }

    // ----- Sahiplik ve kimlik -----

    [Fact]
    public async Task Baska_kullanici_hedefi_tokeni_koşuyu_ve_sonuclari_goremez_degistiremez_404()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("runner-attacker@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);
        var runId = (await ReadAsync(await owner.PostAsync($"/api/automation/{projectId}/test-runs", null))).GetProperty("id").GetInt32();
        var before = _factory.TargetRequests.Count;

        Assert.Equal(HttpStatusCode.NotFound, (await PutSettingsAsync(attacker, projectId, "https://1.1.1.1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutTokenAsync(attacker, projectId, "saldirgan-token")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.DeleteAsync($"/api/automation/{projectId}/target-token")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.PostAsync($"/api/automation/{projectId}/test-runs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/test-runs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/test-runs/{runId}")).StatusCode);

        // Sahibin ayarları ve token'ı bozulmadı, hedefe istek gitmedi
        var settings = await GetSettingsAsync(owner, projectId);
        Assert.Equal(Target, settings.GetProperty("targetBaseUrl").GetString());
        Assert.True(settings.GetProperty("targetTokenConfigured").GetBoolean());
        Assert.Equal(before, _factory.TargetRequests.Count);

        await CleanUpAsync(owner, projectId);
    }

    [Fact]
    public async Task Baska_projenin_koşu_kimligiyle_ayrinti_istenirse_404_doner()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-crossproject@example.com");
        var projectA = await UploadAsync(owner);
        var projectB = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectA, Target);
        var runOfA = (await ReadAsync(await owner.PostAsync($"/api/automation/{projectA}/test-runs", null))).GetProperty("id").GetInt32();

        var response = await owner.GetAsync($"/api/automation/{projectB}/test-runs/{runOfA}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await CleanUpAsync(owner, projectA, projectB);
    }

    [Fact]
    public async Task Tokensiz_istekler_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-anon@example.com");
        var projectId = await UploadAsync(owner);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/automation/{projectId}/test-runs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/test-runs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/test-runs/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutTokenAsync(anonymous, projectId, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/automation/{projectId}/target-token")).StatusCode);
    }

    // ----- Temizlik -----

    [Fact]
    public async Task Proje_silinince_koşular_ve_sonuclari_da_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("runner-delete@example.com");
        var projectId = await UploadAsync(owner);
        await PutSettingsAsync(owner, projectId, Target);
        await PutTokenAsync(owner, projectId, TargetToken);
        await RunToCompletionAsync(owner, projectId);

        using (var before = _factory.Services.CreateScope())
        {
            var db = before.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotEmpty(db.TestRuns.Where(r => r.ProjectId == projectId));
            Assert.NotEmpty(db.TestRunResults.Where(r => db.TestRuns.Any(t => t.Id == r.TestRunId && t.ProjectId == projectId)));
        }

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);

        using var after = _factory.Services.CreateScope();
        var dbAfter = after.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(dbAfter.TestRuns.Where(r => r.ProjectId == projectId));
        Assert.Empty(dbAfter.TestRunResults.Where(r => !dbAfter.TestRuns.Any(t => t.Id == r.TestRunId)));
    }
}
