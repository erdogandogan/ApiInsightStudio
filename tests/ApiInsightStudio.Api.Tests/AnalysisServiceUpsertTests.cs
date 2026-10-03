using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Services;
using Microsoft.EntityFrameworkCore;
using EndpointModel = ApiInsightStudio.Api.Models.Endpoint;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Aynı proje yeniden analiz edilince tek bir sonucun yerinde güncellendiğini doğrular.</summary>
public class AnalysisServiceUpsertTests
{
    private static async Task<AnalysisResult> AnalyzeAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        return await new AnalysisService(context).AnalyzeProjectAsync(projectId);
    }

    private static int CountResults(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        return context.AnalysisResults.Count(a => a.ProjectId == projectId);
    }

    [Fact]
    public async Task Tekrar_analiz_yeni_satir_eklemez_ayni_kaydi_gunceller()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Summary: ""));

        var first = await AnalyzeAsync(db, projectId);
        var second = await AnalyzeAsync(db, projectId);
        var third = await AnalyzeAsync(db, projectId);

        Assert.Equal(1, CountResults(db, projectId));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, third.Id);
    }

    [Fact]
    public async Task Uyarilar_birikmez_yeniden_yazilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Summary: ""));

        await AnalyzeAsync(db, projectId);
        await AnalyzeAsync(db, projectId);

        using var context = db.CreateContext();
        Assert.Equal(1, context.Warnings.Count());
    }

    [Fact]
    public async Task Tekrar_analiz_guncel_durumu_yansitir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Summary: ""));
        var first = await AnalyzeAsync(db, projectId);
        Assert.Equal(90, first.Score);

        // Açıklama sonradan doldurulunca skor ve uyarı güncellenmeli
        using (var context = db.CreateContext())
        {
            var endpoint = context.Endpoints.Single(e => e.ProjectId == projectId);
            endpoint.Summary = "Artık açıklama var";
            context.SaveChanges();
        }

        var second = await AnalyzeAsync(db, projectId);

        Assert.Equal(100, second.Score);
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task Tekrar_analiz_CreatedAt_degerini_ilerletir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        var first = await AnalyzeAsync(db, projectId);
        var firstTime = first.CreatedAt;
        await Task.Delay(20);
        var second = await AnalyzeAsync(db, projectId);

        Assert.True(second.CreatedAt > firstTime);
    }

    [Fact]
    public async Task Mevcut_kopya_kayitlar_temizlenir_en_yenisi_kalir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec(Summary: ""));

        int newestId;
        using (var context = db.CreateContext())
        {
            var endpointId = context.Endpoints.Single(e => e.ProjectId == projectId).Id;
            var old1 = new AnalysisResult { ProjectId = projectId, Score = 10, CreatedAt = DateTime.UtcNow.AddDays(-3) };
            var old2 = new AnalysisResult { ProjectId = projectId, Score = 20, CreatedAt = DateTime.UtcNow.AddDays(-2) };
            var newest = new AnalysisResult { ProjectId = projectId, Score = 30, CreatedAt = DateTime.UtcNow.AddDays(-1) };
            foreach (var analysis in new[] { old1, old2, newest })
                analysis.Warnings.Add(new Warning { EndpointId = endpointId, Message = "eski", Severity = "Low" });
            context.AnalysisResults.AddRange(old1, old2, newest);
            context.SaveChanges();
            newestId = newest.Id;
        }

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(newestId, result.Id);
        Assert.Equal(1, CountResults(db, projectId));
        using var check = db.CreateContext();
        var warning = Assert.Single(check.Warnings);
        Assert.NotEqual("eski", warning.Message);
    }

    [Fact]
    public async Task Baska_projelerin_sonuclarina_dokunmaz()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var projectA = db.SeedProject(userId, new EndpointSpec(Summary: ""));
        var projectB = db.SeedProject(userId, new EndpointSpec(Summary: ""));

        await AnalyzeAsync(db, projectB);
        await AnalyzeAsync(db, projectA);
        await AnalyzeAsync(db, projectA);

        Assert.Equal(1, CountResults(db, projectA));
        Assert.Equal(1, CountResults(db, projectB));
        using var context = db.CreateContext();
        Assert.Equal(2, context.Warnings.Count());
    }

    [Fact]
    public async Task Endpoint_olmayan_proje_bos_ama_gecerli_sonuc_uretir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        var result = await AnalyzeAsync(db, projectId);

        Assert.Equal(100, result.Score);
        Assert.Equal(1, CountResults(db, projectId));
    }
}
