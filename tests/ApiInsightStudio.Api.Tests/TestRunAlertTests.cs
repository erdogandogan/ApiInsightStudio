using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>TEST_FAILURES kuralı: kenar tetikleme, atlananlar, kapsam (her işleyici yalnızca kendi kurallarını yönetir).</summary>
public class TestRunAlertTests
{
    private static int AddCompletedRun(TestDb db, int projectId, int passed, int failed, int skipped = 0, string status = TestRun.StatusCompleted)
    {
        using var context = db.CreateContext();
        var run = new TestRun
        {
            ProjectId = projectId, Status = status, Passed = passed, Failed = failed, Skipped = skipped,
            Total = passed + failed + skipped
        };
        context.TestRuns.Add(run);
        context.SaveChanges();
        return run.Id;
    }

    private static async Task HandleAsync(TestDb db, int projectId, int runId, int passed = 0, int failed = 0, int skipped = 0)
    {
        await using var context = db.CreateContext();
        var handler = new TestRunCompletedHandler(context, new OutboxEventPublisher(context), new AlertRuleEvaluator());
        await handler.HandleAsync(new TestRunCompleted(projectId, runId, passed, failed, skipped, DateTime.UtcNow));
        await context.SaveChangesAsync();
    }

    private static List<Alert> Alerts(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        return context.Alerts.Where(a => a.ProjectId == projectId).OrderBy(a => a.Id).ToList();
    }

    private static int RaisedEvents(TestDb db)
    {
        using var context = db.CreateContext();
        return context.OutboxMessages.Count(m => m.Type == nameof(AlertRaised));
    }

    private static async Task HandleRunAsync(TestDb db, int projectId, int passed, int failed, int skipped = 0, string status = TestRun.StatusCompleted)
    {
        var runId = AddCompletedRun(db, projectId, passed, failed, skipped, status);
        await HandleAsync(db, projectId, runId, passed, failed, skipped);
    }

    private static void AddOpenAlert(TestDb db, int projectId, string ruleCode)
    {
        using var context = db.CreateContext();
        context.Alerts.Add(new Alert { ProjectId = projectId, RuleCode = ruleCode, Message = "m", Severity = "High", Status = Alert.StatusOpen });
        context.SaveChanges();
    }

    // ----- Kural -----

    [Fact]
    public async Task Esigi_asan_basarisizlik_orani_Acik_TEST_FAILURES_uyarisi_ve_AlertRaised_olayi_uretir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, passed: 7, failed: 3); // %30 > %20

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(AlertRuleCodes.TestFailures, alert.RuleCode);
        Assert.Equal(Alert.StatusOpen, alert.Status);
        Assert.Equal("High", alert.Severity);
        Assert.Equal(1, RaisedEvents(db));
    }

    [Fact]
    public async Task Oran_esige_esitse_uyari_acilmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, passed: 8, failed: 2); // tam %20

        Assert.Empty(Alerts(db, projectId));
        Assert.Equal(0, RaisedEvents(db));
    }

    [Fact]
    public async Task Esik_proje_ayarindan_okunur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        TestRunFixtures.SetSettings(db, projectId, thresholdPercent: 50);

        await HandleRunAsync(db, projectId, passed: 6, failed: 4); // %40: varsayılan eşikte (20) açardı, 50'de açmaz

        Assert.Empty(Alerts(db, projectId));
    }

    [Fact]
    public async Task Proje_kapaliysa_uyari_acilmaz_ve_mevcut_uyari_olduğu_gibi_kalir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        TestRunFixtures.SetSettings(db, projectId, enabled: false);
        AddOpenAlert(db, projectId, AlertRuleCodes.TestFailures);

        await HandleRunAsync(db, projectId, passed: 0, failed: 10);
        await HandleRunAsync(db, projectId, passed: 10, failed: 0);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(Alert.StatusOpen, alert.Status); // kapalı projede ne açılır ne çözülür
        Assert.Equal(0, RaisedEvents(db));
    }

    // ----- Kenar tetikleme -----

    [Fact]
    public async Task Ardisik_yuksek_basarisizlik_ayni_uyariyi_cogaltmaz_ve_tek_olay_uretir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, 5, 5);
        await HandleRunAsync(db, projectId, 4, 6);
        await HandleRunAsync(db, projectId, 0, 10);

        Assert.Single(Alerts(db, projectId));
        Assert.Equal(1, RaisedEvents(db));
    }

    [Fact]
    public async Task Acik_uyari_surerken_mesaj_guncel_orani_yansitir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        await HandleRunAsync(db, projectId, 7, 3);

        await HandleRunAsync(db, projectId, 0, 10);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Contains("%100", alert.Message);
    }

    [Fact]
    public async Task Oran_esigin_altina_inince_uyari_Cozuldu_olur_ve_tekrar_bozulunca_yenisi_acilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, 5, 5);   // açıldı
        await HandleRunAsync(db, projectId, 10, 0);  // çözüldü
        await HandleRunAsync(db, projectId, 5, 5);   // yeniden bozuldu

        var alerts = Alerts(db, projectId);
        Assert.Equal(2, alerts.Count);
        Assert.Equal(Alert.StatusResolved, alerts[0].Status);
        Assert.NotNull(alerts[0].ResolvedAt);
        Assert.Equal(Alert.StatusOpen, alerts[1].Status);
        Assert.Equal(2, RaisedEvents(db));
    }

    // ----- Atlananlar / geçersiz koşular -----

    [Fact]
    public async Task Hic_test_calismadiysa_durum_degismez_acik_uyari_cozulmez_ve_yeni_uyari_acilmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        await HandleRunAsync(db, projectId, 5, 5); // uyarı açık

        await HandleRunAsync(db, projectId, passed: 0, failed: 0, skipped: 12); // hepsi atlandı

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(Alert.StatusOpen, alert.Status); // "bilgi yok": çözülmez
    }

    [Fact]
    public async Task Hepsi_atlanan_ilk_koşu_uyari_acmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, 0, 0, skipped: 11);

        Assert.Empty(Alerts(db, projectId));
    }

    [Fact]
    public async Task Basarisiz_biten_koşu_degerlendirilmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleRunAsync(db, projectId, 0, 10, status: TestRun.StatusFailed);

        Assert.Empty(Alerts(db, projectId));
    }

    [Fact]
    public async Task Olayda_sayilar_yerine_veritabanindaki_koşu_esas_alinir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var runId = AddCompletedRun(db, projectId, passed: 10, failed: 0);

        await HandleAsync(db, projectId, runId, passed: 0, failed: 10); // olay yalan söylüyor

        Assert.Empty(Alerts(db, projectId));
    }

    [Fact]
    public async Task Olmayan_veya_baska_projeye_ait_koşu_yok_sayilir()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var mine = db.SeedProject(userId);
        var other = db.SeedProject(userId);
        var othersRun = AddCompletedRun(db, other, 0, 10);

        await HandleAsync(db, mine, runId: 999999, failed: 10);
        await HandleAsync(db, mine, othersRun, failed: 10);

        Assert.Empty(Alerts(db, mine));
        Assert.Empty(Alerts(db, other));
    }

    // ----- Kapsam: her işleyici yalnızca kendi kurallarını yönetir -----

    [Fact]
    public async Task Analiz_isleyicisi_temiz_analizde_TEST_FAILURES_uyarisini_cozmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec()); // temiz uç nokta: skor 100
        AddOpenAlert(db, projectId, AlertRuleCodes.TestFailures);

        await using (var context = db.CreateContext())
            await TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId);

        var alert = Assert.Single(Alerts(db, projectId));
        Assert.Equal(AlertRuleCodes.TestFailures, alert.RuleCode);
        Assert.Equal(Alert.StatusOpen, alert.Status); // eski davranışta burada yanlışlıkla Çözüldü olurdu
    }

    [Fact]
    public async Task Analiz_isleyicisi_kendi_kurallarini_yine_cozer()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());
        AddOpenAlert(db, projectId, AlertRuleCodes.LowScore);
        AddOpenAlert(db, projectId, AlertRuleCodes.MissingAuth);

        await using (var context = db.CreateContext())
            await TestServices.CreateAnalysisService(context).AnalyzeProjectAsync(projectId);

        Assert.All(Alerts(db, projectId), a => Assert.Equal(Alert.StatusResolved, a.Status));
    }

    [Fact]
    public async Task Test_koşusu_isleyicisi_analiz_uyarilarini_cozmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        AddOpenAlert(db, projectId, AlertRuleCodes.LowScore);
        AddOpenAlert(db, projectId, AlertRuleCodes.MissingAuth);

        await HandleRunAsync(db, projectId, passed: 10, failed: 0); // temiz koşu

        Assert.All(Alerts(db, projectId), a => Assert.Equal(Alert.StatusOpen, a.Status));
    }

    [Fact]
    public async Task Test_koşusu_isleyicisi_yalnizca_TEST_FAILURES_uyarisini_cozer()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        AddOpenAlert(db, projectId, AlertRuleCodes.LowScore);
        AddOpenAlert(db, projectId, AlertRuleCodes.TestFailures);

        await HandleRunAsync(db, projectId, passed: 10, failed: 0);

        var alerts = Alerts(db, projectId);
        Assert.Equal(Alert.StatusOpen, alerts.Single(a => a.RuleCode == AlertRuleCodes.LowScore).Status);
        Assert.Equal(Alert.StatusResolved, alerts.Single(a => a.RuleCode == AlertRuleCodes.TestFailures).Status);
    }

    [Fact]
    public async Task Uzlastirici_yonetmedigi_bir_kural_koduyla_gelen_adayi_reddeder()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        await using var context = db.CreateContext();
        var reconciler = new AlertReconciler(context, new OutboxEventPublisher(context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => reconciler.ReconcileAsync(
            projectId,
            new[] { new AlertCandidate(AlertRuleCodes.TestFailures, "m", "High") },
            AlertRuleCodes.AnalysisRules));
    }

    // ----- Bildirim hattına bağlanma -----

    [Fact]
    public async Task Yeni_TEST_FAILURES_uyarisi_webhook_icin_teslimat_acar()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        using (var context = db.CreateContext())
        {
            context.ProjectAutomationSettings.Add(new ProjectAutomationSettings
            {
                ProjectId = projectId, WebhookUrl = "https://hooks.example.com/alerts"
            });
            context.SaveChanges();
        }
        var runId = AddCompletedRun(db, projectId, passed: 5, failed: 5);

        await using (var context = db.CreateContext())
        {
            using var provider = TestServices.BuildProvider(context, services =>
            {
                services.AddSingleton<IEventHandler<TestRunCompleted>>(sp =>
                    new TestRunCompletedHandler(context, sp.GetRequiredService<IEventPublisher>(), sp.GetRequiredService<AlertRuleEvaluator>()));
                services.AddSingleton<IEventHandler<AlertRaised>>(sp => new AlertRaisedHandler(context, TimeProvider.System));
            });
            sp_Publish(provider, new TestRunCompleted(projectId, runId, 5, 5, 0, DateTime.UtcNow));
            await context.SaveChangesAsync();
            await provider.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();
        }

        using var check = db.CreateContext();
        var delivery = Assert.Single(check.NotificationDeliveries.Where(d => d.ProjectId == projectId).ToList());
        Assert.Equal("webhook", delivery.Channel);
        Assert.Contains(AlertRuleCodes.TestFailures, delivery.PayloadJson);
    }

    private static void sp_Publish(ServiceProvider provider, TestRunCompleted evt) =>
        provider.GetRequiredService<IEventPublisher>().Publish(evt);
}
