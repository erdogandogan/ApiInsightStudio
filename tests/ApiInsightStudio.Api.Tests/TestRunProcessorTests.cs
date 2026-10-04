using System.Net;
using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.TestRunner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Kuyruk işleyici: sıra, kurtarma, hata yalıtımı, saklama sınırı ve olay → uyarı zinciri.</summary>
public class TestRunProcessorTests
{
    private static Task<int> ProcessAsync(
        TestDb db, HttpMessageHandler handler, TestClock? clock = null, CancellationToken cancellationToken = default)
    {
        clock ??= new TestClock();
        var context = db.CreateContext();
        var provider = TestServices.BuildProvider(context, services =>
        {
            services.AddSingleton<IEventHandler<AnalysisCompleted>, AnalysisCompletedHandler>();
            services.AddSingleton<IEventHandler<TestRunCompleted>>(sp =>
                new TestRunCompletedHandler(context, sp.GetRequiredService<IEventPublisher>(), sp.GetRequiredService<AlertRuleEvaluator>()));
        }, clock);

        var processor = new TestRunProcessor(
            context,
            TestRunFixtures.CreateExecutor(context, handler, clock),
            provider.GetRequiredService<OutboxDispatcher>(),
            clock,
            NullLogger<TestRunProcessor>.Instance);

        return Dispose(processor.ProcessPendingAsync(cancellationToken), context, provider);
    }

    private static async Task<int> Dispose(Task<int> work, AppDbContext context, ServiceProvider provider)
    {
        try
        {
            return await work;
        }
        finally
        {
            await context.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private static async Task<int> TypicalProjectWithPendingRunAsync(TestDb db, int userId, bool allowMutating = false)
    {
        var projectId = db.SeedProject(userId, TestRunFixtures.TypicalEndpoints());
        await TestRunFixtures.GenerateScenariosAsync(db, projectId);
        TestRunFixtures.SetSettings(db, projectId, allowMutating: allowMutating);
        return projectId;
    }

    private static TestRun Run(TestDb db, int runId)
    {
        using var context = db.CreateContext();
        return context.TestRuns.Single(r => r.Id == runId);
    }

    // ----- Sıra ve yaşam döngüsü -----

    [Fact]
    public async Task Bekleyen_koşuyu_calistirir_Completed_yapar_ve_bir_donusu_dondurur()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        var runId = TestRunFixtures.AddRun(db, projectId);

        var processed = await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

        Assert.Equal(1, processed);
        var run = Run(db, runId);
        Assert.Equal(TestRun.StatusCompleted, run.Status);
        Assert.NotNull(run.StartedAt);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(3, run.Passed);
    }

    [Fact]
    public async Task Kuyruk_bossa_sifir_doner_ve_biten_koşu_tekrar_calistirilmaz()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        TestRunFixtures.AddRun(db, projectId);
        var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

        await ProcessAsync(db, handler);
        var callsAfterFirst = handler.Requests.Count;
        var second = await ProcessAsync(db, handler);

        Assert.Equal(0, second);
        Assert.Equal(callsAfterFirst, handler.Requests.Count);
    }

    [Fact]
    public async Task Tek_cagrida_yalnizca_en_eski_bekleyen_koşu_calisir()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var first = await TypicalProjectWithPendingRunAsync(db, userId);
        var second = await TypicalProjectWithPendingRunAsync(db, userId);
        var firstRun = TestRunFixtures.AddRun(db, first);
        var secondRun = TestRunFixtures.AddRun(db, second);

        var processed = await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

        Assert.Equal(1, processed);
        Assert.Equal(TestRun.StatusCompleted, Run(db, firstRun).Status);
        Assert.Equal(TestRun.StatusPending, Run(db, secondRun).Status); // sıradaki sonraki turu bekler
    }

    [Fact]
    public async Task Tamamlanmis_ve_basarisiz_koşulara_dokunulmaz()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        var completed = TestRunFixtures.AddRun(db, projectId, TestRun.StatusCompleted);
        var failed = TestRunFixtures.AddRun(db, projectId, TestRun.StatusFailed);
        var handler = new FakeTargetHandler(FakeTargetHandler.TypicalApi);

        var processed = await ProcessAsync(db, handler);

        Assert.Equal(0, processed);
        Assert.Empty(handler.Requests);
        Assert.Equal(TestRun.StatusCompleted, Run(db, completed).Status);
        Assert.Equal(TestRun.StatusFailed, Run(db, failed).Status);
    }

    [Fact]
    public async Task Onceden_iptal_edilmis_belirteç_istisna_atmadan_sifir_doner()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        var runId = TestRunFixtures.AddRun(db, projectId);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var processed = await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi), cancellationToken: cts.Token);

        Assert.Equal(0, processed);
        Assert.Equal(TestRun.StatusPending, Run(db, runId).Status); // dokunulmadı, sonra çalışır
    }

    // ----- Yarıda kalan koşular -----

    [Fact]
    public async Task Gecmisten_beri_Running_kalan_koşu_Failed_olarak_kurtarilir_taze_olana_dokunulmaz()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var staleProject = db.SeedProject(userId);
        var freshProject = db.SeedProject(userId);
        var clock = new TestClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = TestRunFixtures.AddRun(db, staleProject, TestRun.StatusRunning, startedAt: now - TestRunProcessor.StuckAfter - TimeSpan.FromMinutes(1));
        var fresh = TestRunFixtures.AddRun(db, freshProject, TestRun.StatusRunning, startedAt: now - TimeSpan.FromMinutes(5));

        await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi), clock);

        var recovered = Run(db, stale);
        Assert.Equal(TestRun.StatusFailed, recovered.Status);
        Assert.Contains("yarıda kesildi", recovered.Error);
        Assert.Equal(now, recovered.CompletedAt);
        Assert.Equal(TestRun.StatusRunning, Run(db, fresh).Status);
    }

    [Fact]
    public async Task Kurtarilan_projede_yeni_koşu_baslatilabilir()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        var clock = new TestClock();
        TestRunFixtures.AddRun(db, projectId, TestRun.StatusRunning,
            startedAt: clock.GetUtcNow().UtcDateTime - TestRunProcessor.StuckAfter - TimeSpan.FromMinutes(1));

        await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi), clock); // kurtarır

        var newRunId = TestRunFixtures.AddRun(db, projectId); // benzersiz "aktif koşu" dizini artık engel olmaz
        Assert.True(newRunId > 0);
    }

    // ----- Hata yalıtımı -----

    [Fact]
    public async Task Beklenmeyen_istisna_koşuyu_Failed_yapar_yarim_sonuclari_kaydetmez_ve_islemeye_devam_eder()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var broken = await TypicalProjectWithPendingRunAsync(db, userId);
        var healthy = await TypicalProjectWithPendingRunAsync(db, userId);
        var brokenRun = TestRunFixtures.AddRun(db, broken);
        var healthyRun = TestRunFixtures.AddRun(db, healthy);
        var handler = new FakeTargetHandler(_ => throw new InvalidOperationException("içeride GIZLI-DETAY vardı"));

        var processed = await ProcessAsync(db, handler);

        Assert.Equal(1, processed);
        var run = Run(db, brokenRun);
        Assert.Equal(TestRun.StatusFailed, run.Status);
        Assert.Equal("Beklenmeyen hata: InvalidOperationException.", run.Error);
        Assert.DoesNotContain("GIZLI-DETAY", run.Error);
        using (var context = db.CreateContext())
            Assert.Empty(context.TestRunResults.Where(r => r.TestRunId == brokenRun));

        // Bozuk koşu sırayı tıkamadı: sonraki tur sağlıklı koşuyu çalıştırır
        var next = await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));
        Assert.Equal(1, next);
        Assert.Equal(TestRun.StatusCompleted, Run(db, healthyRun).Status);
    }

    [Fact]
    public async Task Basarisiz_koşudan_sonra_ayni_projede_yeni_koşu_baslatilabilir()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        TestRunFixtures.AddRun(db, projectId);

        await ProcessAsync(db, new FakeTargetHandler(_ => throw new InvalidOperationException("x")));

        Assert.True(TestRunFixtures.AddRun(db, projectId) > 0);
    }

    // ----- Benzersiz aktif koşu -----

    [Fact]
    public void Ayni_projede_ikinci_aktif_koşu_veritabaninca_reddedilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        TestRunFixtures.AddRun(db, projectId, TestRun.StatusPending);

        Assert.ThrowsAny<DbUpdateException>(() => TestRunFixtures.AddRun(db, projectId, TestRun.StatusPending));
        Assert.ThrowsAny<DbUpdateException>(() => TestRunFixtures.AddRun(db, projectId, TestRun.StatusRunning));
    }

    [Fact]
    public void Farkli_projelerde_ve_biten_koşularda_birden_fazla_kayit_serbesttir()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var a = db.SeedProject(userId);
        var b = db.SeedProject(userId);

        TestRunFixtures.AddRun(db, a, TestRun.StatusPending);
        TestRunFixtures.AddRun(db, b, TestRun.StatusPending);   // başka proje
        TestRunFixtures.AddRun(db, a, TestRun.StatusCompleted); // biten koşular sınırsız
        TestRunFixtures.AddRun(db, a, TestRun.StatusCompleted);
        TestRunFixtures.AddRun(db, a, TestRun.StatusFailed);

        using var context = db.CreateContext();
        Assert.Equal(5, context.TestRuns.Count());
    }

    // ----- Saklama sınırı -----

    [Fact]
    public async Task Her_projede_yalnizca_son_20_koşu_ve_sonuclari_saklanir_digerlerine_dokunulmaz()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var projectId = await TypicalProjectWithPendingRunAsync(db, userId);
        var otherProject = db.SeedProject(userId);

        using (var context = db.CreateContext())
        {
            for (var i = 0; i < 25; i++)
            {
                var old = new TestRun { ProjectId = projectId, Status = TestRun.StatusCompleted, Total = 1, Passed = 1 };
                old.Results.Add(new TestRunResult { Title = $"eski-{i}", Method = "GET", Path = "/x", Outcome = TestRunResult.OutcomePassed });
                context.TestRuns.Add(old);
            }
            for (var i = 0; i < 3; i++)
                context.TestRuns.Add(new TestRun { ProjectId = otherProject, Status = TestRun.StatusCompleted });
            context.SaveChanges();
        }
        var pending = TestRunFixtures.AddRun(db, projectId);

        await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

        using var check = db.CreateContext();
        var kept = check.TestRuns.Where(r => r.ProjectId == projectId).OrderBy(r => r.Id).Select(r => r.Id).ToList();
        Assert.Equal(TestRunProcessor.RunsToKeepPerProject, kept.Count);
        Assert.Contains(pending, kept);                                            // yeni koşu saklandı
        Assert.Equal(3, check.TestRuns.Count(r => r.ProjectId == otherProject));   // diğer proje etkilenmedi

        // Silinen koşuların sonuçları da silindi (yetim satır yok)
        var orphanResults = check.TestRunResults.Count(r => !check.TestRuns.Any(t => t.Id == r.TestRunId));
        Assert.Equal(0, orphanResults);
        Assert.DoesNotContain(check.TestRunResults.ToList(), r => r.Title == "eski-0"); // en eski silindi
        Assert.Contains(check.TestRunResults.ToList(), r => r.Title == "eski-24");       // yeni olanlar kaldı
    }

    // ----- Olay → uyarı → teslimat zinciri -----

    [Fact]
    public async Task Yuksek_basarisizlik_orani_TEST_FAILURES_uyarisi_acar_ve_olay_hemen_islenir()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        var runId = TestRunFixtures.AddRun(db, projectId);

        await ProcessAsync(db, new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        using var context = db.CreateContext();
        var alert = Assert.Single(context.Alerts.Where(a => a.ProjectId == projectId).ToList());
        Assert.Equal(AlertRuleCodes.TestFailures, alert.RuleCode);
        Assert.Equal(Alert.StatusOpen, alert.Status);
        var completed = Assert.Single(context.OutboxMessages.Where(m => m.Type == nameof(TestRunCompleted)).ToList());
        Assert.NotNull(completed.ProcessedAt); // dispatcher koşu biter bitmez işledi
        Assert.Equal(3, Run(db, runId).Failed);
    }

    [Fact]
    public async Task Basarili_koşu_uyari_acmaz()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        TestRunFixtures.AddRun(db, projectId);

        await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

        using var context = db.CreateContext();
        Assert.Empty(context.Alerts.Where(a => a.ProjectId == projectId));
    }

    [Fact]
    public async Task Sorun_duzelince_sonraki_koşuda_uyari_Cozuldu_olur()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        TestRunFixtures.AddRun(db, projectId);
        await ProcessAsync(db, new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        TestRunFixtures.AddRun(db, projectId);
        await ProcessAsync(db, new FakeTargetHandler(FakeTargetHandler.TypicalApi));

        using var context = db.CreateContext();
        var alert = Assert.Single(context.Alerts.Where(a => a.ProjectId == projectId).ToList());
        Assert.Equal(Alert.StatusResolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
    }

    [Fact]
    public async Task Ardisik_basarisiz_koşular_ayni_uyariyi_cogaltmaz()
    {
        using var db = new TestDb();
        var projectId = await TypicalProjectWithPendingRunAsync(db, db.SeedUser());
        for (var i = 0; i < 3; i++)
        {
            TestRunFixtures.AddRun(db, projectId);
            await ProcessAsync(db, new FakeTargetHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        }

        using var context = db.CreateContext();
        Assert.Single(context.Alerts.Where(a => a.ProjectId == projectId));
        Assert.Single(context.OutboxMessages.Where(m => m.Type == nameof(AlertRaised)));
    }
}
