using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AI önerisi inceleme akışı (taslak → onay/ret/düzenle) ve denetim izi, gerçek HTTP hattı üzerinden.</summary>
public class AiReviewApiTests : IClassFixture<ApiFactory>
{
    private const string OpenApiDocument = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Onay testi", "version": "1.0.0" },
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

    public AiReviewApiTests(ApiFactory factory) => _factory = factory;

    private async Task<(int ProjectId, int EndpointId)> UploadAsync(HttpClient owner)
    {
        var upload = await owner.PostAsJsonAsync("/api/project/upload", new { openApiContent = OpenApiDocument });
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var projectId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetInt32();
        return (projectId, WithDb(db => db.Endpoints.Where(e => e.ProjectId == projectId && e.Method == "POST").Select(e => e.Id).Single()));
    }

    private T WithDb<T>(Func<AppDbContext, T> read)
    {
        using var scope = _factory.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private void WithDb(Action<AppDbContext> act) => WithDb<int>(db => { act(db); return 0; });

    private static async Task<int> GenerateAsync(HttpClient client, int projectId, int endpointId)
    {
        var response = await client.PostAsync($"/api/project/{projectId}/endpoint/{endpointId}/generate-ai-description", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal(ApiFactory.OllamaReplyText, body.GetProperty("description").GetString());   // eski istemci uyumu
        return body.GetProperty("suggestionId").GetInt32();
    }

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient c, int projectId, int id, object? body = null) =>
        c.PostAsJsonAsync($"/api/automation/{projectId}/ai-suggestions/{id}/approve", body ?? new { });

    private static Task<HttpResponseMessage> RejectAsync(HttpClient c, int projectId, int id, object? body = null) =>
        c.PostAsJsonAsync($"/api/automation/{projectId}/ai-suggestions/{id}/reject", body ?? new { });

    private string? AiSummary(int endpointId) => WithDb(db => db.Endpoints.AsNoTracking().Single(e => e.Id == endpointId).AiSummary);

    private AiSuggestion Suggestion(int id) => WithDb(db => db.AiSuggestions.AsNoTracking().Single(s => s.Id == id));

    private static async Task<JsonElement> GetAsync(HttpClient c, string url) => await c.GetFromJsonAsync<JsonElement>(url);

    // ---------- Taslak oluşturma ----------

    [Fact]
    public async Task Uretim_taslak_olusturur_ve_AiSummaryyi_degistirmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-draft@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);

        var id = await GenerateAsync(owner, projectId, endpointId);

        Assert.Null(AiSummary(endpointId));
        var s = Suggestion(id);
        Assert.Equal("Pending", s.Status);
        Assert.Equal(ApiFactory.OllamaReplyText, s.Content);
        Assert.Equal("POST", s.Method);
        Assert.Equal("/items", s.Path);
        Assert.Equal("qwen2.5:7b", s.Model);
        Assert.Equal("describe-endpoint/v1", s.PromptVersion);
        Assert.Null(s.FinalContent);
        Assert.Null(s.ReviewedAt);

        var list = await GetAsync(owner, $"/api/automation/{projectId}/ai-suggestions");
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("Pending", item.GetProperty("status").GetString());
        Assert.Equal("qwen2.5:7b", item.GetProperty("model").GetString());
        Assert.Equal("describe-endpoint/v1", item.GetProperty("promptVersion").GetString());
    }

    [Fact]
    public async Task Yeni_uretim_eski_bekleyeni_Superseded_yapar_ve_tek_Pending_kalir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-supersede@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);

        var first = await GenerateAsync(owner, projectId, endpointId);
        var second = await GenerateAsync(owner, projectId, endpointId);

        Assert.NotEqual(first, second);
        Assert.Equal("Superseded", Suggestion(first).Status);
        Assert.Equal("Pending", Suggestion(second).Status);
        Assert.Equal(1, WithDb(db => db.AiSuggestions.Count(s => s.EndpointId == endpointId && s.Status == "Pending")));

        // Geçersiz kılınan öneri artık onaylanamaz
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(owner, projectId, first)).StatusCode);
        Assert.Null(AiSummary(endpointId));

        var superseded = await GetAsync(owner, $"/api/automation/{projectId}/audit?action=ai.suggestion.superseded");
        Assert.Single(superseded.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Onaylanmis_oneriden_sonra_yeni_uretim_AiSummaryyi_degistirmez_ta_ki_onaylanana_kadar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-keepold@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var first = await GenerateAsync(owner, projectId, endpointId);
        await ApproveAsync(owner, projectId, first, new { editedContent = "İlk onaylı metin." });

        var second = await GenerateAsync(owner, projectId, endpointId);

        Assert.Equal("İlk onaylı metin.", AiSummary(endpointId));   // yayımlanan metin taslakla ezilmez
        Assert.Equal("Approved", Suggestion(first).Status);
        Assert.Equal("Pending", Suggestion(second).Status);
    }

    // ---------- Onay ----------

    [Fact]
    public async Task Onay_ozgun_metni_AiSummaryye_yazar_ve_kaydi_tutar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-approve@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        var response = await ApproveAsync(owner, projectId, id, new { note = "Doğru." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ApiFactory.OllamaReplyText, AiSummary(endpointId));
        var s = Suggestion(id);
        Assert.Equal("Approved", s.Status);
        Assert.Null(s.FinalContent);                 // düzenleme yok
        Assert.NotNull(s.ReviewedAt);
        Assert.NotNull(s.ReviewedByUserId);
        Assert.Equal("Doğru.", s.ReviewNote);
        Assert.Equal(s.CreatedByUserId, s.ReviewedByUserId);   // tek sahipli model: ayrı onaylayan yok, kayıt tutuluyor
    }

    [Fact]
    public async Task Duzenleyerek_onay_son_metni_yazar_ozgun_metin_korunur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-edit@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        var response = await ApproveAsync(owner, projectId, id, new { editedContent = "  Yeni ürün kaydı oluşturur.  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Yeni ürün kaydı oluşturur.", AiSummary(endpointId));   // kırpılmış
        var s = Suggestion(id);
        Assert.Equal(ApiFactory.OllamaReplyText, s.Content);                 // modelin özgün metni değişmez
        Assert.Equal("Yeni ürün kaydı oluşturur.", s.FinalContent);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Yeni ürün kaydı oluşturur.", body.GetProperty("finalContent").GetString());
        var audit = await GetAsync(owner, $"/api/automation/{projectId}/audit?action=ai.suggestion.approved");
        Assert.Contains("edited=true", audit.GetProperty("items")[0].GetProperty("details").GetString());
    }

    [Fact]
    public async Task Duzenlenmemis_ayni_metinle_onay_duzenlendi_sayilmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-same@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        await ApproveAsync(owner, projectId, id, new { editedContent = ApiFactory.OllamaReplyText });

        Assert.Null(Suggestion(id).FinalContent);
        Assert.Equal(ApiFactory.OllamaReplyText, AiSummary(endpointId));
    }

    [Fact]
    public async Task Onay_Summary_skor_ve_uyarilari_degistirmez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-score@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var before = await GetAsync(owner, $"/api/project/{projectId}/dashboard");
        var id = await GenerateAsync(owner, projectId, endpointId);

        await ApproveAsync(owner, projectId, id);

        var after = await GetAsync(owner, $"/api/project/{projectId}/dashboard");
        Assert.Equal(before.GetProperty("kaliteSkoru").GetInt32(), after.GetProperty("kaliteSkoru").GetInt32());
        Assert.Equal(before.GetProperty("warnings").GetArrayLength(), after.GetProperty("warnings").GetArrayLength());
        Assert.Equal(string.Empty, WithDb(db => db.Endpoints.AsNoTracking().Single(e => e.Id == endpointId).Summary));

        // Onaylı açıklama dashboard'da, ilgili uyarının uç noktasında görünür
        var warning = after.GetProperty("warnings").EnumerateArray()
            .First(w => w.GetProperty("endpointId").GetInt32() == endpointId);
        Assert.Equal(ApiFactory.OllamaReplyText, warning.GetProperty("endpointAiSummary").GetString());
    }

    [Fact]
    public async Task Ayni_oneri_ikinci_kez_onaylanamaz_ve_reddedilemez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-twice@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);
        await ApproveAsync(owner, projectId, id);

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(owner, projectId, id, new { editedContent = "Başka metin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RejectAsync(owner, projectId, id)).StatusCode);

        Assert.Equal(ApiFactory.OllamaReplyText, AiSummary(endpointId));   // ikinci onay metni değiştirmedi
        Assert.Equal("Approved", Suggestion(id).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Bos_duzenlenmis_metin_reddedilir(string edited)
    {
        var owner = await _factory.CreateAuthenticatedClientAsync($"review-empty-{edited.Length}@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        var response = await ApproveAsync(owner, projectId, id, new { editedContent = edited });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Pending", Suggestion(id).Status);
        Assert.Null(AiSummary(endpointId));
    }

    [Fact]
    public async Task Cok_uzun_metin_ve_not_reddedilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-long@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await ApproveAsync(owner, projectId, id, new { editedContent = new string('x', AiSuggestion.MaxContentLength + 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ApproveAsync(owner, projectId, id, new { note = new string('x', AiSuggestion.MaxNoteLength + 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RejectAsync(owner, projectId, id, new { note = new string('x', AiSuggestion.MaxNoteLength + 1) })).StatusCode);
        Assert.Equal("Pending", Suggestion(id).Status);
    }

    [Fact]
    public async Task Govdesiz_onay_istegi_ozgun_metni_onaylar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-nobody@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        var response = await owner.PostAsync($"/api/automation/{projectId}/ai-suggestions/{id}/approve", null);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnsupportedMediaType, response.StatusCode.ToString());
        if (response.StatusCode == HttpStatusCode.OK)
            Assert.Equal(ApiFactory.OllamaReplyText, AiSummary(endpointId));
    }

    // ---------- Ret ----------

    [Fact]
    public async Task Ret_hicbir_yere_yazmaz_ve_notu_tutar()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-reject@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        var response = await RejectAsync(owner, projectId, id, new { note = "Yanlış anlatıyor." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(AiSummary(endpointId));
        var s = Suggestion(id);
        Assert.Equal("Rejected", s.Status);
        Assert.Equal("Yanlış anlatıyor.", s.ReviewNote);
        Assert.NotNull(s.ReviewedAt);
        Assert.Null(s.FinalContent);

        // Reddedilen öneri sonradan onaylanamaz
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(owner, projectId, id)).StatusCode);
        Assert.Null(AiSummary(endpointId));
    }

    [Fact]
    public async Task Reddedildikten_sonra_yeni_oneri_uretilebilir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-regen@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var first = await GenerateAsync(owner, projectId, endpointId);
        await RejectAsync(owner, projectId, first);

        var second = await GenerateAsync(owner, projectId, endpointId);

        Assert.Equal("Rejected", Suggestion(first).Status);   // Superseded olmaz, ret kaydı korunur
        Assert.Equal("Pending", Suggestion(second).Status);
    }

    // ---------- Liste ----------

    [Fact]
    public async Task Liste_duruma_gore_suzulur_ve_gecersiz_durum_400_verir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-filter@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var a = await GenerateAsync(owner, projectId, endpointId);
        await RejectAsync(owner, projectId, a);
        var b = await GenerateAsync(owner, projectId, endpointId);

        var pending = await GetAsync(owner, $"/api/automation/{projectId}/ai-suggestions?status=Pending");
        var rejected = await GetAsync(owner, $"/api/automation/{projectId}/ai-suggestions?status=Rejected");
        var all = await GetAsync(owner, $"/api/automation/{projectId}/ai-suggestions");

        Assert.Equal(b, Assert.Single(pending.EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(a, Assert.Single(rejected.EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal(2, all.GetArrayLength());
        Assert.Equal(b, all[0].GetProperty("id").GetInt32());   // en yeni önce
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/automation/{projectId}/ai-suggestions?status=Foo")).StatusCode);
    }

    // ---------- Sahiplik ve kimlik doğrulama ----------

    [Fact]
    public async Task Baska_kullanici_listeleyemez_onaylayamaz_reddedemez_denetimi_goremez()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-owner@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("review-attacker@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);

        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/ai-suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(attacker, projectId, id, new { editedContent = "Sahte" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(attacker, projectId, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/automation/{projectId}/audit/verify")).StatusCode);

        Assert.Equal("Pending", Suggestion(id).Status);
        Assert.Null(AiSummary(endpointId));
    }

    [Fact]
    public async Task Baska_projenin_oneri_kimligi_kendi_projesi_altinda_calismaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-cross-a@example.com");
        var other = await _factory.CreateAuthenticatedClientAsync("review-cross-b@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var (otherProject, _) = await UploadAsync(other);
        var id = await GenerateAsync(owner, projectId, endpointId);

        // Saldırgan kendi projesinin yolunda başkasının öneri kimliğini deniyor
        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(other, otherProject, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(other, otherProject, id)).StatusCode);
        Assert.Equal("Pending", Suggestion(id).Status);
    }

    [Fact]
    public async Task Tokensiz_istekler_401_alir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-anon@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/ai-suggestions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApproveAsync(anonymous, projectId, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RejectAsync(anonymous, projectId, id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/automation/{projectId}/audit/verify")).StatusCode);
        Assert.Equal("Pending", Suggestion(id).Status);
    }

    [Fact]
    public async Task Olmayan_oneri_404_verir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("review-missing@example.com");
        var (projectId, _) = await UploadAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(owner, projectId, 999999)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(owner, projectId, 999999)).StatusCode);
    }

    // ---------- Denetim izi ----------

    [Fact]
    public async Task Tum_akis_denetim_izine_sirayla_yazilir_ve_zincir_gecerlidir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-flow@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var a = await GenerateAsync(owner, projectId, endpointId);
        await RejectAsync(owner, projectId, a);
        var b = await GenerateAsync(owner, projectId, endpointId);
        await ApproveAsync(owner, projectId, b);
        await owner.PostAsync($"/api/analysis/{projectId}/analyze", null);

        var page = await GetAsync(owner, $"/api/automation/{projectId}/audit?take=100");
        var actions = page.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("action").GetString()).Reverse().ToArray();

        Assert.Equal(new[]
        {
            "project.uploaded",
            "ai.suggestion.created",
            "ai.suggestion.rejected",
            "ai.suggestion.created",
            "ai.suggestion.approved",
            "analysis.completed"
        }, actions);
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextBefore").ValueKind);

        var verify = await GetAsync(owner, $"/api/automation/{projectId}/audit/verify");
        Assert.True(verify.GetProperty("valid").GetBoolean());
        Assert.Equal(6, verify.GetProperty("entryCount").GetInt32());
        Assert.Equal(page.GetProperty("items")[0].GetProperty("hash").GetString(), verify.GetProperty("headHash").GetString());
    }

    [Fact]
    public async Task Denetim_sayfalanir_ve_onekle_suzulur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-page@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        for (var i = 0; i < 3; i++)
            await GenerateAsync(owner, projectId, endpointId);   // 1 upload + 3 created + 2 superseded = 6 kayıt

        var first = await GetAsync(owner, $"/api/automation/{projectId}/audit?take=4");
        Assert.Equal(4, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextBefore").GetInt32();
        Assert.Equal(first.GetProperty("items")[3].GetProperty("sequence").GetInt32(), cursor);

        var second = await GetAsync(owner, $"/api/automation/{projectId}/audit?take=4&before={cursor}");
        Assert.Equal(2, second.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextBefore").ValueKind);
        Assert.All(second.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("sequence").GetInt32() < cursor));

        var created = await GetAsync(owner, $"/api/automation/{projectId}/audit?action=ai.suggestion.created");
        Assert.Equal(3, created.GetProperty("items").GetArrayLength());
        var allAi = await GetAsync(owner, $"/api/automation/{projectId}/audit?action=ai.suggestion");
        Assert.Equal(5, allAi.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Take_siniri_asilamaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-take@example.com");
        var (projectId, _) = await UploadAsync(owner);

        var page = await GetAsync(owner, $"/api/automation/{projectId}/audit?take=100000");

        Assert.Equal(1, page.GetProperty("items").GetArrayLength());   // yalnızca upload kaydı var; hata yok
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/automation/{projectId}/audit?take=-5")).StatusCode);
    }

    [Fact]
    public async Task Veritabaninda_kurcalanan_kayit_HTTP_dogrulamasinda_yakalanir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-tamper@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);
        await ApproveAsync(owner, projectId, id);
        Assert.True((await GetAsync(owner, $"/api/automation/{projectId}/audit/verify")).GetProperty("valid").GetBoolean());

        WithDb(db => db.Database.ExecuteSqlInterpolated(
            $"UPDATE AuditLogs SET Details = 'sahte' WHERE ProjectId = {projectId} AND Action = 'ai.suggestion.approved'"));

        var verify = await GetAsync(owner, $"/api/automation/{projectId}/audit/verify");
        Assert.False(verify.GetProperty("valid").GetBoolean());
        Assert.Equal(3, verify.GetProperty("firstInvalidSequence").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(verify.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Denetim_icin_guncelleme_ve_silme_uc_noktasi_yoktur()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-readonly@example.com");
        var (projectId, _) = await UploadAsync(owner);

        foreach (var url in new[] { $"/api/automation/{projectId}/audit", $"/api/automation/{projectId}/audit/1", $"/api/automation/{projectId}/audit/verify" })
        {
            Assert.True((await owner.DeleteAsync(url)).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, url);
            Assert.True((await owner.PutAsJsonAsync(url, new { })).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, url);
            Assert.True((await owner.PostAsJsonAsync(url, new { })).StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, url);
        }

        Assert.Equal(1, WithDb(db => db.AuditLogs.Count(a => a.ProjectId == projectId)));
    }

    [Fact]
    public async Task Ayar_degisiklikleri_denetlenir_ama_gizli_bilgi_ve_tam_adres_yazilmaz()
    {
        const string webhookUrl = "https://1.1.1.1/hook/cok-gizli-yol-123?key=gizli-anahtar-456";
        const string target = "https://8.8.8.8";
        const string token = "hedef-token-cok-gizli-789";

        var owner = await _factory.CreateAuthenticatedClientAsync("audit-secrets@example.com");
        var (projectId, _) = await UploadAsync(owner);

        var settings = await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings", new
        {
            scoreThreshold = 70, notifyOnMissingAuth = true, enabled = true, webhookUrl, targetBaseUrl = target
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        var newSecret = (await settings.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("newWebhookSecret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"/api/automation/{projectId}/target-token", new { token })).StatusCode);
        var rotated = await owner.PostAsync($"/api/automation/{projectId}/webhook/secret", null);
        var rotatedSecret = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("webhookSecret").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/automation/{projectId}/target-token")).StatusCode);

        var raw = await (await owner.GetAsync($"/api/automation/{projectId}/audit?take=100")).Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray().ToList();
        var actions = items.Select(i => i.GetProperty("action").GetString()).ToList();

        Assert.Contains("settings.updated", actions);
        Assert.Contains("target.token.set", actions);
        Assert.Contains("webhook.secret.rotated", actions);
        Assert.Contains("target.token.cleared", actions);

        // Gizli bilgi, tam adres, yol, sorgu, sır ve token hiçbir yerde yok (cevapta da veritabanında da)
        var dbDump = string.Join("|", WithDb(db => db.AuditLogs.Where(a => a.ProjectId == projectId)
            .Select(a => a.Details + "|" + a.Subject + "|" + a.Action).ToList()));
        foreach (var forbidden in new[] { "cok-gizli-yol-123", "gizli-anahtar-456", "hedef-token-cok-gizli-789", newSecret, rotatedSecret, "https://" })
        {
            Assert.DoesNotContain(forbidden, raw);
            Assert.DoesNotContain(forbidden, dbDump);
        }

        var updated = items.Single(i => i.GetProperty("action").GetString() == "settings.updated").GetProperty("details").GetString()!;
        Assert.Contains("webhook=1.1.1.1", updated);
        Assert.Contains("target=8.8.8.8", updated);
        Assert.Contains("scoreThreshold=70", updated);
        Assert.True((await GetAsync(owner, $"/api/automation/{projectId}/audit/verify")).GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Basarisiz_istekler_denetim_kaydi_birakmaz()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-failed@example.com");
        var attacker = await _factory.CreateAuthenticatedClientAsync("audit-failed-attacker@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);
        var before = WithDb(db => db.AuditLogs.Count(a => a.ProjectId == projectId));

        await ApproveAsync(attacker, projectId, id);                                   // 404
        await ApproveAsync(owner, projectId, id, new { editedContent = " " });         // 400
        await RejectAsync(owner, projectId, 999999);                                   // 404
        await owner.PutAsJsonAsync($"/api/automation/{projectId}/settings", new { scoreThreshold = 500 });   // 400

        Assert.Equal(before, WithDb(db => db.AuditLogs.Count(a => a.ProjectId == projectId)));
    }

    [Fact]
    public async Task Proje_silinince_oneriler_ve_denetim_kayitlari_da_silinir()
    {
        var owner = await _factory.CreateAuthenticatedClientAsync("audit-delete@example.com");
        var (projectId, endpointId) = await UploadAsync(owner);
        var id = await GenerateAsync(owner, projectId, endpointId);
        await ApproveAsync(owner, projectId, id);
        Assert.True(WithDb(db => db.AuditLogs.Any(a => a.ProjectId == projectId)));

        Assert.Equal(HttpStatusCode.OK, (await owner.DeleteAsync($"/api/project/{projectId}")).StatusCode);

        Assert.False(WithDb(db => db.AiSuggestions.Any(s => s.ProjectId == projectId)));
        Assert.False(WithDb(db => db.AuditLogs.Any(a => a.ProjectId == projectId)));
    }
}
