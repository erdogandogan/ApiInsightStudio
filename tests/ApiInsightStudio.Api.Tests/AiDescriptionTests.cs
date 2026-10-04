using System.Security.Claims;
using ApiInsightStudio.Api.Controllers;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AI çıktısının önce onay bekleyen taslak olduğunu; onaydan sonra yalnızca AiSummary'ye yazıldığını, skoru ve uyarıları etkilemediğini doğrular.</summary>
public class AiDescriptionTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond());
    }

    /// <summary>OpenAI uyumlu /v1/chat/completions cevabı.</summary>
    private static HttpResponseMessage OllamaReply(string text) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { role = "assistant", content = text } } }
            }))
        };

    private static ProjectController CreateController(
        Data.AppDbContext context, int userId, Func<HttpResponseMessage> ollama)
    {
        var aiService = new AiService(new HttpClient(new FakeHandler(ollama)), Options.Create(new AiOptions()));
        var controller = new ProjectController(
            context,
            new ConfigurationBuilder().Build(),
            TestServices.CreateAnalysisService(context),
            new TestGenerationService(context),
            aiService,
            new ApiInsightStudio.Api.Audit.AuditTrail(context, TimeProvider.System));

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static (int UserId, int ProjectId, int EndpointId) SeedAnalyzedProject(TestDb db)
    {
        var userId = db.SeedUser();
        var projectId = db.SeedProject(userId, new EndpointSpec(Summary: ""));

        using var context = db.CreateContext();
        TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId).GetAwaiter().GetResult();
        var endpointId = context.Endpoints.Single(e => e.ProjectId == projectId).Id;
        return (userId, projectId, endpointId);
    }

    [Fact]
    public async Task AI_ciktisi_taslak_olur_AiSummary_ve_Summary_degismez_skor_ve_uyari_ayni_kalir()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId, () => OllamaReply("Ürünleri listeler."));
            var result = await controller.GenerateAiDescription(projectId, endpointId);
            Assert.IsType<OkObjectResult>(result);
        }

        await using var check = db.CreateContext();
        var endpoint = check.Endpoints.Single(e => e.Id == endpointId);
        Assert.Null(endpoint.AiSummary);                       // onaylanmadan yayımlanmaz
        Assert.Equal(string.Empty, endpoint.Summary);

        var suggestion = Assert.Single(check.AiSuggestions);
        Assert.Equal("Ürünleri listeler.", suggestion.Content);
        Assert.Equal(Models.AiSuggestion.StatusPending, suggestion.Status);
        Assert.Equal("qwen2.5:7b", suggestion.Model);
        Assert.Equal(AiService.PromptVersion, suggestion.PromptVersion);
        Assert.Equal(userId, suggestion.CreatedByUserId);

        var analysis = check.AnalysisResults.Single(a => a.ProjectId == projectId);
        Assert.Equal(90, analysis.Score);
        var warning = Assert.Single(check.Warnings);
        Assert.Equal("Endpoint açıklaması eksik", warning.Message);
    }

    [Fact]
    public async Task Dashboard_onaylanmis_AI_aciklamasini_dondurur_taslagi_dondurmez()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using var context = db.CreateContext();
        var controller = CreateController(context, userId, () => OllamaReply("Ürünleri listeler."));
        await controller.GenerateAiDescription(projectId, endpointId);

        // Taslak onaylanmadan dashboard AI açıklaması göstermez
        var before = Assert.IsType<DashboardReportDto>(Assert.IsType<OkObjectResult>(await controller.GetDashboard(projectId)).Value);
        Assert.Null(Assert.Single(before.Warnings).EndpointAiSummary);

        var suggestionId = context.AiSuggestions.Single().Id;
        var review = new AiReviewController(context, new ApiInsightStudio.Api.Audit.AuditTrail(context, TimeProvider.System), TimeProvider.System)
        {
            ControllerContext = controller.ControllerContext
        };
        Assert.IsType<OkObjectResult>(await review.Approve(projectId, suggestionId, null));

        var report = Assert.IsType<DashboardReportDto>(Assert.IsType<OkObjectResult>(await controller.GetDashboard(projectId)).Value);
        Assert.Equal(90, report.KaliteSkoru);
        var warning = Assert.Single(report.Warnings);
        Assert.Equal("Ürünleri listeler.", warning.EndpointAiSummary);
    }

    [Fact]
    public async Task Ollama_kapaliysa_503_doner_ve_AiSummary_bos_kalir()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId,
                () => throw new HttpRequestException("bağlantı reddedildi"));
            var result = await controller.GenerateAiDescription(projectId, endpointId);
            Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        await using var check = db.CreateContext();
        Assert.Null(check.Endpoints.Single(e => e.Id == endpointId).AiSummary);
        Assert.Empty(check.AiSuggestions);
    }

    [Fact]
    public async Task Servis_hata_kodu_donerse_502_doner_ve_model_adini_hatirlatir()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId,
                () => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            var result = (ObjectResult)await controller.GenerateAiDescription(projectId, endpointId);

            Assert.Equal(502, result.StatusCode);
            Assert.Contains("qwen2.5:7b", System.Text.Json.JsonSerializer.Serialize(result.Value));
        }

        await using var check = db.CreateContext();
        Assert.Null(check.Endpoints.Single(e => e.Id == endpointId).AiSummary);
        Assert.Empty(check.AiSuggestions);
    }

    [Fact]
    public async Task Bos_cevap_502_doner_ve_AiSummary_bos_kalir()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId, () => OllamaReply("   "));
            var result = await controller.GenerateAiDescription(projectId, endpointId);
            Assert.Equal(502, Assert.IsType<ObjectResult>(result).StatusCode);
        }

        await using var check = db.CreateContext();
        Assert.Null(check.Endpoints.Single(e => e.Id == endpointId).AiSummary);
        Assert.Empty(check.AiSuggestions);
    }

    [Fact]
    public async Task Yeniden_analiz_onaylanmis_AI_aciklamasini_silmez()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId, () => OllamaReply("Ürünleri listeler."));
            await controller.GenerateAiDescription(projectId, endpointId);
            var suggestionId = context.AiSuggestions.Single().Id;
            var review = new AiReviewController(context, new ApiInsightStudio.Api.Audit.AuditTrail(context, TimeProvider.System), TimeProvider.System)
            {
                ControllerContext = controller.ControllerContext
            };
            await review.Approve(projectId, suggestionId, null);
        }

        await using (var context = db.CreateContext())
            await TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId);

        await using var check = db.CreateContext();
        Assert.Equal("Ürünleri listeler.", check.Endpoints.Single(e => e.Id == endpointId).AiSummary);
    }
}
