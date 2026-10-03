using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using ApiInsightStudio.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Analiz → olay (outbox) → kural → uyarı akışını SQLite üzerinde uçtan uca doğrular.</summary>
public class AutomationEventFlowTests
{
    /// <summary>İki kötü endpoint: her biri -10 (açıklama) ve -15 (hata kodu) → skor 50, eşik 60'ın altında.</summary>
    private static EndpointSpec[] LowScoreEndpoints() => new[]
    {
        new EndpointSpec(Path: "/a", Summary: "", StatusCodesOrNull: new[] { "200" }),
        new EndpointSpec(Path: "/b", Summary: "", StatusCodesOrNull: new[] { "200" })
    };

    /// <summary>Skor 80 (eşiğin üstünde) ama kritik uç noktada kimlik doğrulama yok.</summary>
    private static EndpointSpec MissingAuthEndpoint() => new(Method: "POST", AuthType: null);

    private static async Task AnalyzeAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        await TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId);
    }

    private static List<Alert> Alerts(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        return context.Alerts.Where(a => a.ProjectId == projectId).OrderBy(a => a.Id).ToList();
    }

    private static List<OutboxMessage> Outbox(TestDb db, string? type = null)
    {
        using var context = db.CreateContext();
        var query = context.OutboxMessages.AsQueryable();
        if (type is not null) query = query.Where(m => m.Type == type);
        return query.OrderBy(m => m.Id).ToList();
    }

    /// <summary>Projenin tüm endpoint'lerini temiz hale getirir: açıklama ve hata kodu ekler.</summary>
    private static void FixEndpoints(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        foreach (var endpoint in context.Endpoints.Include(e => e.Responses).Where(e => e.ProjectId == projectId))
        {
            endpoint.Summary = "Açıklama var";
            endpoint.AuthType = "Bearer";
            endpoint.Responses.Add(new Response { StatusCode = "400", Description = "d" });
        }
        context.SaveChanges();
    }

    [Fact]
    public async Task Analiz_AnalysisCompleted_olayini_outboxa_yazar_ve_islenir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        await AnalyzeAsync(db, projectId);

        var message = Assert.Single(Outbox(db, nameof(AnalysisCompleted)));
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
        Assert.Contains($"\"ProjectId\":{projectId}", message.PayloadJson);
    }

    [Fact]
    public async Task Temiz_proje_uyari_uretmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        await AnalyzeAsync(db, projectId);

        Assert.Empty(Alerts(db, projectId));
        Assert.Empty(Outbox(db, nameof(AlertRaised)));
    }

    [Fact]
    public async Task Dusuk_skor_Acik_uyari_ve_AlertRaised_olayi_uretir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());

        await AnalyzeAsync(db, projectId);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(AlertRuleCodes.LowScore, alert.RuleCode);
        Assert.Equal(Alert.StatusOpen, alert.Status);
        Assert.Null(alert.ResolvedAt);

        var raised = Assert.Single(Outbox(db, nameof(AlertRaised)));
        Assert.Contains(AlertRuleCodes.LowScore, raised.PayloadJson);
    }

    [Fact]
    public async Task AlertRaised_isleyicisi_olmadigi_icin_beklemede_kalir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());

        await AnalyzeAsync(db, projectId);

        // Aşama 2'de bildirim işleyicisi gelince bu mesajlar işlenecek.
        var raised = Assert.Single(Outbox(db, nameof(AlertRaised)));
        Assert.Null(raised.ProcessedAt);
    }

    [Fact]
    public async Task Kritik_uc_noktada_kimlik_yoksa_MISSING_AUTH_uyarisi_acilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), MissingAuthEndpoint());

        await AnalyzeAsync(db, projectId);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(AlertRuleCodes.MissingAuth, alert.RuleCode);
        Assert.Equal("High", alert.Severity);
        Assert.Contains("1", alert.Message);
    }

    [Fact]
    public async Task Tekrarlanan_analiz_ayni_uyariyi_cogaltmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());

        await AnalyzeAsync(db, projectId);
        await AnalyzeAsync(db, projectId);
        await AnalyzeAsync(db, projectId);

        Assert.Single(Alerts(db, projectId));
        // Tek uyarı = tek bildirim olayı (Aşama 2'de tek bildirim gidecek)
        Assert.Single(Outbox(db, nameof(AlertRaised)));
        Assert.Equal(3, Outbox(db, nameof(AnalysisCompleted)).Count);
    }

    [Fact]
    public async Task Kural_artik_tutmuyorsa_Acik_uyari_Cozuldu_olur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());
        await AnalyzeAsync(db, projectId);

        FixEndpoints(db, projectId);
        await AnalyzeAsync(db, projectId);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(Alert.StatusResolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
    }

    [Fact]
    public async Task Cozulen_uyari_tekrar_bozulunca_yeni_uyari_acilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());
        await AnalyzeAsync(db, projectId);
        FixEndpoints(db, projectId);
        await AnalyzeAsync(db, projectId);

        using (var context = db.CreateContext())
        {
            foreach (var endpoint in context.Endpoints.Where(e => e.ProjectId == projectId))
                endpoint.Summary = "";
            context.Responses.RemoveRange(context.Responses.Where(r => r.StatusCode == "400"));
            context.SaveChanges();
        }
        await AnalyzeAsync(db, projectId);

        var alerts = Alerts(db, projectId);
        Assert.Equal(2, alerts.Count);
        Assert.Equal(Alert.StatusResolved, alerts[0].Status);
        Assert.Equal(Alert.StatusOpen, alerts[1].Status);
        Assert.Equal(2, Outbox(db, nameof(AlertRaised)).Count);
    }

    [Fact]
    public async Task Esik_proje_ayarindan_okunur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints()); // skor 50

        using (var context = db.CreateContext())
        {
            context.ProjectAutomationSettings.Add(new ProjectAutomationSettings { ProjectId = projectId, ScoreThreshold = 50 });
            context.SaveChanges();
        }
        await AnalyzeAsync(db, projectId);

        Assert.Empty(Alerts(db, projectId)); // 50, eşik 50'nin altında değil
    }

    [Fact]
    public async Task Proje_kapaliysa_uyari_acilmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());

        using (var context = db.CreateContext())
        {
            context.ProjectAutomationSettings.Add(new ProjectAutomationSettings { ProjectId = projectId, Enabled = false });
            context.SaveChanges();
        }
        await AnalyzeAsync(db, projectId);

        Assert.Empty(Alerts(db, projectId));
        Assert.Empty(Outbox(db, nameof(AlertRaised)));
    }

    [Fact]
    public async Task Acik_uyari_surerken_mesaj_guncellenir_ama_yeni_bildirim_olmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), MissingAuthEndpoint());
        await AnalyzeAsync(db, projectId);

        using (var context = db.CreateContext())
        {
            // ikinci kritik uç nokta ekle
            var project = context.Projects.Include(p => p.Endpoints).Single(p => p.Id == projectId);
            project.Endpoints.Add(new Api.Models.Endpoint
            {
                Method = "PUT", Path = "/other", Summary = "x", AuthType = null,
                Responses = { new Response { StatusCode = "400", Description = "d" } }
            });
            context.SaveChanges();
        }
        await AnalyzeAsync(db, projectId);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Contains("2", alert.Message);
        Assert.Single(Outbox(db, nameof(AlertRaised)));
    }

    [Fact]
    public async Task Proje_silinmisse_olay_sessizce_islenir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        using (var context = db.CreateContext())
        {
            // Olay deftere yazıldı ama proje işlenmeden silindi gibi bir durum
            TestServices.BuildProvider(context).GetRequiredService<IEventPublisher>()
                .Publish(new AnalysisCompleted(projectId + 999, DateTime.UtcNow));
            context.SaveChanges();
            var dispatched = await TestServices.BuildProvider(context).GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();
            Assert.Equal(1, dispatched);
        }

        Assert.Empty(Alerts(db, projectId + 999));
    }

    // ----- Başarısız işleyici -----

    private sealed class ThrowingHandler : IEventHandler<AnalysisCompleted>
    {
        private readonly AppDbContext _context;

        public ThrowingHandler(AppDbContext context) => _context = context;

        public Task HandleAsync(AnalysisCompleted @event, CancellationToken cancellationToken = default)
        {
            // Yarım kalan bir yazma: hata sonrası geri alınmalı
            _context.Alerts.Add(new Alert { ProjectId = @event.ProjectId, RuleCode = "PARTIAL", Message = "yarım", Severity = "Low" });
            throw new InvalidOperationException("işleyici bilerek patladı");
        }
    }

    [Fact]
    public async Task Basarisiz_isleyici_deneme_sayisini_ve_hatayi_kaydeder_yarim_yazmayi_geri_alir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());

        await using var context = db.CreateContext();
        using var provider = TestServices.BuildProvider(context, services =>
            services.AddSingleton<IEventHandler<AnalysisCompleted>>(sp => new ThrowingHandler(context)));

        // Analiz isteği hatayla bozulmamalı
        var result = await TestServices.CreateAnalysisService(provider).AnalyzeProjectAsync(projectId);
        Assert.Equal(100, result.Score);

        var message = Assert.Single(Outbox(db, nameof(AnalysisCompleted)));
        Assert.Null(message.ProcessedAt);
        Assert.Equal(1, message.Attempts);
        Assert.Contains("bilerek patladı", message.LastError);
        Assert.Empty(Alerts(db, projectId)); // yarım yazma geri alındı
    }

    [Fact]
    public async Task Basarisiz_mesaj_ilk_hatada_10_sn_sonraya_ertelenir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());
        var clock = new TestClock();

        await using var context = db.CreateContext();
        using var provider = TestServices.BuildProvider(context, services =>
            services.AddSingleton<IEventHandler<AnalysisCompleted>>(sp => new ThrowingHandler(context)), clock);
        await TestServices.CreateAnalysisService(provider).AnalyzeProjectAsync(projectId);

        var message = Assert.Single(Outbox(db, nameof(AnalysisCompleted)));
        Assert.Equal(1, message.Attempts);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddSeconds(10), message.NextAttemptAt);
        Assert.Null(message.DeadAt);
    }

    [Fact]
    public async Task Vakti_gelmeyen_basarisiz_mesaj_tekrar_denenmez_gelince_denenir_ve_bekleme_ikiye_katlanir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());
        var clock = new TestClock();

        await using var context = db.CreateContext();
        using var provider = TestServices.BuildProvider(context, services =>
            services.AddSingleton<IEventHandler<AnalysisCompleted>>(sp => new ThrowingHandler(context)), clock);
        await TestServices.CreateAnalysisService(provider).AnalyzeProjectAsync(projectId);
        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();

        // Vakit gelmeden: yeniden denenmez
        clock.Advance(TimeSpan.FromSeconds(9));
        await dispatcher.DispatchPendingAsync();
        Assert.Equal(1, Assert.Single(Outbox(db, nameof(AnalysisCompleted))).Attempts);

        // Vakit gelince: denenir, bir sonraki bekleme 20 sn
        clock.Advance(TimeSpan.FromSeconds(1));
        await dispatcher.DispatchPendingAsync();
        var message = Assert.Single(Outbox(db, nameof(AnalysisCompleted)));
        Assert.Equal(2, message.Attempts);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddSeconds(20), message.NextAttemptAt);
    }

    [Fact]
    public async Task Mesaj_RetryPolicy_MaxAttempts_denemeden_sonra_olu_olur_ve_bir_daha_denenmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());
        var clock = new TestClock();

        await using var context = db.CreateContext();
        using var provider = TestServices.BuildProvider(context, services =>
            services.AddSingleton<IEventHandler<AnalysisCompleted>>(sp => new ThrowingHandler(context)), clock);
        await TestServices.CreateAnalysisService(provider).AnalyzeProjectAsync(projectId);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher>();
        for (var i = 0; i < RetryPolicy.MaxAttempts + 3; i++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            await dispatcher.DispatchPendingAsync();
        }

        var message = Assert.Single(Outbox(db, nameof(AnalysisCompleted)));
        Assert.Equal(RetryPolicy.MaxAttempts, message.Attempts);
        Assert.NotNull(message.DeadAt);
        Assert.Null(message.NextAttemptAt);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public async Task Basarisiz_bir_mesaj_siradaki_mesajlari_engellemez()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var failingProject = db.SeedProject(userId, new EndpointSpec());
        var goodProject = db.SeedProject(userId, LowScoreEndpoints());

        await using var context = db.CreateContext();

        // İşleyici olmadan analiz et: iki proje için AnalysisCompleted olayları işlenmeden bekler
        using (var withoutHandlers = TestServices.BuildProvider(context, _ => { }))
        {
            var analysis = TestServices.CreateAnalysisService(withoutHandlers);
            await analysis.AnalyzeProjectAsync(failingProject);
            await analysis.AnalyzeProjectAsync(goodProject);
        }
        Assert.All(Outbox(db, nameof(AnalysisCompleted)), m => Assert.Null(m.ProcessedAt));

        // Yalnızca failingProject için patlayan işleyici
        using var provider = TestServices.BuildProvider(context, services =>
        {
            services.AddSingleton<IEventHandler<AnalysisCompleted>>(sp =>
                new SelectiveHandler(context, failingProject, sp.GetRequiredService<IEventPublisher>(),
                    sp.GetRequiredService<AlertRuleEvaluator>()));
        });
        await provider.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();

        var messages = Outbox(db, nameof(AnalysisCompleted));
        Assert.Equal(1, messages.Single(m => m.PayloadJson.Contains($"\"ProjectId\":{failingProject}")).Attempts);
        Assert.NotNull(messages.Single(m => m.PayloadJson.Contains($"\"ProjectId\":{goodProject}")).ProcessedAt);
        Assert.Single(Alerts(db, goodProject));
    }

    private sealed class SelectiveHandler : IEventHandler<AnalysisCompleted>
    {
        private readonly int _failingProjectId;
        private readonly AnalysisCompletedHandler _inner;

        public SelectiveHandler(AppDbContext context, int failingProjectId, IEventPublisher events, AlertRuleEvaluator evaluator)
        {
            _failingProjectId = failingProjectId;
            _inner = new AnalysisCompletedHandler(context, events, evaluator);
        }

        public Task HandleAsync(AnalysisCompleted @event, CancellationToken cancellationToken = default)
        {
            if (@event.ProjectId == _failingProjectId)
                throw new InvalidOperationException("bu proje için bilerek patladı");
            return _inner.HandleAsync(@event, cancellationToken);
        }
    }
}
