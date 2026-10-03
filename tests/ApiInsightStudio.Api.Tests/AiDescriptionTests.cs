using System.Security.Claims;
using ApiInsightStudio.Api.Controllers;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AI açıklamasının yalnızca AiSummary'ye yazıldığını, skoru ve uyarıları etkilemediğini doğrular.</summary>
public class AiDescriptionTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond());
    }

    private static HttpResponseMessage OllamaReply(string text) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { response = text }))
        };

    private static ProjectController CreateController(
        Data.AppDbContext context, int userId, Func<HttpResponseMessage> ollama)
    {
        var aiService = new AiService(new HttpClient(new FakeHandler(ollama)));
        var controller = new ProjectController(
            context,
            new ConfigurationBuilder().Build(),
            TestServices.CreateAnalysisService(context),
            new TestGenerationService(context),
            aiService);

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
    public async Task AI_aciklamasi_AiSummarye_yazilir_Summary_skor_ve_uyari_degismez()
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
        Assert.Equal("Ürünleri listeler.", endpoint.AiSummary);
        Assert.Equal(string.Empty, endpoint.Summary);

        var analysis = check.AnalysisResults.Single(a => a.ProjectId == projectId);
        Assert.Equal(90, analysis.Score);
        var warning = Assert.Single(check.Warnings);
        Assert.Equal("Endpoint açıklaması eksik", warning.Message);
    }

    [Fact]
    public async Task Dashboard_kaydedilmis_AI_aciklamasini_sunucudan_dondurur()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using var context = db.CreateContext();
        var controller = CreateController(context, userId, () => OllamaReply("Ürünleri listeler."));
        await controller.GenerateAiDescription(projectId, endpointId);

        var result = await controller.GetDashboard(projectId);

        var report = Assert.IsType<DashboardReportDto>(Assert.IsType<OkObjectResult>(result).Value);
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
    }

    [Fact]
    public async Task Yeniden_analiz_kaydedilmis_AI_aciklamasini_silmez()
    {
        using var db = new TestDb();
        var (userId, projectId, endpointId) = SeedAnalyzedProject(db);

        await using (var context = db.CreateContext())
        {
            var controller = CreateController(context, userId, () => OllamaReply("Ürünleri listeler."));
            await controller.GenerateAiDescription(projectId, endpointId);
        }

        await using (var context = db.CreateContext())
            await TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId);

        await using var check = db.CreateContext();
        Assert.Equal("Ürünleri listeler.", check.Endpoints.Single(e => e.Id == endpointId).AiSummary);
    }
}
