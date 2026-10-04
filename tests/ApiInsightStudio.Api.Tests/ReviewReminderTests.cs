using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>PENDING_REVIEW: 24 saatten uzun süredir onay bekleyen AI önerisi hatırlatması (kenar tetiklemeli, saat kontrollü).</summary>
public class ReviewReminderTests
{
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static (TestDb Db, int ProjectId, int EndpointId) NewProject()
    {
        var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), new EndpointSpec());
        using var context = db.CreateContext();
        return (db, projectId, context.Endpoints.Single(e => e.ProjectId == projectId).Id);
    }

    private static int AddSuggestion(
        TestDb db, int projectId, int endpointId, DateTime createdAt, string status = AiSuggestion.StatusPending)
    {
        using var context = db.CreateContext();
        var suggestion = new AiSuggestion
        {
            ProjectId = projectId, EndpointId = endpointId, Method = "GET", Path = "/items", Content = "x",
            Model = "m", PromptVersion = "v", Status = status, CreatedAt = createdAt
        };
        context.AiSuggestions.Add(suggestion);
        context.SaveChanges();
        return suggestion.Id;
    }

    private static void SetStatus(TestDb db, int suggestionId, string status)
    {
        using var context = db.CreateContext();
        context.AiSuggestions.Single(s => s.Id == suggestionId).Status = status;
        context.SaveChanges();
    }

    private static async Task EvaluateAsync(TestDb db, TestClock clock, Action<IServiceCollection>? configure = null)
    {
        await using var context = db.CreateContext();
        await using var provider = TestServices.BuildProvider(context, configure ?? (_ => { }), clock);
        var service = new ReviewReminderService(context, provider.GetRequiredService<IEventPublisher>(), clock);
        await service.EvaluateAsync();
    }

    private static List<Alert> Alerts(TestDb db, int projectId, string? rule = AlertRuleCodes.PendingReview)
    {
        using var context = db.CreateContext();
        return context.Alerts.AsNoTracking()
            .Where(a => a.ProjectId == projectId && (rule == null || a.RuleCode == rule))
            .OrderBy(a => a.Id).ToList();
    }

    private static int RaisedEvents(TestDb db)
    {
        using var context = db.CreateContext();
        return context.OutboxMessages.Count(m => m.Type == nameof(AlertRaised));
    }

    [Fact]
    public async Task Oneri_yoksa_uyari_acilmaz()
    {
        var (db, projectId, _) = NewProject();
        using (db)
        {
            await EvaluateAsync(db, new TestClock());

            Assert.Empty(Alerts(db, projectId, null));
            Assert.Equal(0, RaisedEvents(db));
        }
    }

    [Fact]
    public async Task Yirmi_dort_saati_dolmayan_oneri_uyari_acmaz()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - Day + TimeSpan.FromSeconds(1));

            await EvaluateAsync(db, clock);

            Assert.Empty(Alerts(db, projectId));
        }
    }

    [Fact]
    public async Task Tam_yirmi_dort_saat_gecikme_sayilmaz_bir_tik_sonrasi_sayilir()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            var now = clock.GetUtcNow().UtcDateTime;
            var id = AddSuggestion(db, projectId, endpointId, now - Day);

            await EvaluateAsync(db, clock);
            Assert.Empty(Alerts(db, projectId));        // tam 24 saat: eşiği AŞMALI

            clock.Advance(TimeSpan.FromTicks(1));
            await EvaluateAsync(db, clock);
            Assert.Single(Alerts(db, projectId));
            Assert.NotEqual(0, id);
        }
    }

    [Fact]
    public async Task Gecikmis_oneri_uyariyi_acar_ve_olay_bir_kez_yayimlanir()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - Day - TimeSpan.FromHours(1));

            await EvaluateAsync(db, clock);

            var alert = Assert.Single(Alerts(db, projectId));
            Assert.Equal(Alert.StatusOpen, alert.Status);
            Assert.Equal("Low", alert.Severity);
            Assert.Contains("1 AI önerisi", alert.Message);
            Assert.Contains("24 saat", alert.Message);
            Assert.Equal(1, RaisedEvents(db));
        }
    }

    [Fact]
    public async Task Kenar_tetiklemeli_tekrar_degerlendirme_yeni_bildirim_uretmez_ama_sayiyi_gunceller()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            var old = clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(30);
            AddSuggestion(db, projectId, endpointId, old);

            await EvaluateAsync(db, clock);
            await EvaluateAsync(db, clock);
            await EvaluateAsync(db, clock);
            Assert.Equal(1, RaisedEvents(db));

            // Başka bir uç noktada ikinci gecikmiş öneri: aynı uyarı güncellenir, yeni bildirim yok
            int secondEndpoint;
            using (var context = db.CreateContext())
            {
                var endpoint = new Models.Endpoint { ProjectId = projectId, Method = "POST", Path = "/other", Summary = "s" };
                context.Endpoints.Add(endpoint);
                context.SaveChanges();
                secondEndpoint = endpoint.Id;
            }
            AddSuggestion(db, projectId, secondEndpoint, old);

            await EvaluateAsync(db, clock);

            var alert = Assert.Single(Alerts(db, projectId));
            Assert.Contains("2 AI önerisi", alert.Message);
            Assert.Equal(1, RaisedEvents(db));
        }
    }

    [Theory]
    [InlineData(AiSuggestion.StatusApproved)]
    [InlineData(AiSuggestion.StatusRejected)]
    [InlineData(AiSuggestion.StatusSuperseded)]
    public async Task Incelenmis_veya_gecersiz_oneriler_sayilmaz(string status)
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(10), status);

            await EvaluateAsync(db, clock);

            Assert.Empty(Alerts(db, projectId));
        }
    }

    [Theory]
    [InlineData(AiSuggestion.StatusApproved)]
    [InlineData(AiSuggestion.StatusRejected)]
    [InlineData(AiSuggestion.StatusSuperseded)]
    public async Task Hepsi_incelenince_uyari_cozulur_sonra_yine_gecikirse_yeni_uyari_acilir(string reviewedStatus)
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            var id = AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(30));
            await EvaluateAsync(db, clock);
            Assert.Single(Alerts(db, projectId));

            SetStatus(db, id, reviewedStatus);
            await EvaluateAsync(db, clock);

            var resolved = Assert.Single(Alerts(db, projectId));
            Assert.Equal(Alert.StatusResolved, resolved.Status);
            Assert.NotNull(resolved.ResolvedAt);

            // Yeni bir öneri 24 saat bekleyince yeniden uyarı (ve bildirim) gelir
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime);
            await EvaluateAsync(db, clock);
            Assert.Equal(Alert.StatusResolved, Assert.Single(Alerts(db, projectId)).Status);   // henüz taze: yeni uyarı yok

            clock.Advance(Day + TimeSpan.FromMinutes(1));
            await EvaluateAsync(db, clock);

            var all = Alerts(db, projectId);
            Assert.Equal(2, all.Count);
            Assert.Equal(Alert.StatusOpen, all[1].Status);
            Assert.Equal(2, RaisedEvents(db));
        }
    }

    [Fact]
    public async Task Saat_ilerleyince_taze_oneri_gecikir()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime);

            await EvaluateAsync(db, clock);
            Assert.Empty(Alerts(db, projectId));

            clock.Advance(TimeSpan.FromHours(23));
            await EvaluateAsync(db, clock);
            Assert.Empty(Alerts(db, projectId));

            clock.Advance(TimeSpan.FromHours(2));
            await EvaluateAsync(db, clock);
            Assert.Single(Alerts(db, projectId));
        }
    }

    [Fact]
    public async Task Projeler_birbirinden_bagimsiz_degerlendirilir()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var late = db.SeedProject(userId, new EndpointSpec());
        var fresh = db.SeedProject(userId, new EndpointSpec());
        int lateEndpoint, freshEndpoint;
        using (var context = db.CreateContext())
        {
            lateEndpoint = context.Endpoints.Single(e => e.ProjectId == late).Id;
            freshEndpoint = context.Endpoints.Single(e => e.ProjectId == fresh).Id;
        }

        var clock = new TestClock();
        AddSuggestion(db, late, lateEndpoint, clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(48));
        AddSuggestion(db, fresh, freshEndpoint, clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(1));

        await EvaluateAsync(db, clock);

        Assert.Single(Alerts(db, late));
        Assert.Empty(Alerts(db, fresh));
    }

    [Fact]
    public async Task Otomasyon_kapaliysa_uyari_acilmaz_ve_mevcut_uyari_oldugu_gibi_kalir()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            var clock = new TestClock();
            var id = AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(30));
            await EvaluateAsync(db, clock);
            Assert.Single(Alerts(db, projectId));

            TestRunFixtures.SetSettings(db, projectId, enabled: false);
            SetStatus(db, id, AiSuggestion.StatusApproved);   // normalde uyarıyı çözerdi
            await EvaluateAsync(db, clock);

            Assert.Equal(Alert.StatusOpen, Assert.Single(Alerts(db, projectId)).Status);   // kapalıyken dokunulmaz
            Assert.Equal(1, RaisedEvents(db));
        }
    }

    [Fact]
    public async Task Diger_kurallarin_uyarilarina_dokunmaz()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            using (var context = db.CreateContext())
            {
                context.Alerts.Add(new Alert { ProjectId = projectId, RuleCode = AlertRuleCodes.LowScore, Message = "m", Severity = "Medium" });
                context.Alerts.Add(new Alert { ProjectId = projectId, RuleCode = AlertRuleCodes.TestFailures, Message = "m", Severity = "High" });
                context.SaveChanges();
            }

            var clock = new TestClock();
            await EvaluateAsync(db, clock);                                  // öneri yok → yalnızca PENDING_REVIEW yönetilir
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - TimeSpan.FromDays(2));
            await EvaluateAsync(db, clock);

            var all = Alerts(db, projectId, null);
            Assert.Equal(3, all.Count);
            Assert.All(all.Where(a => a.RuleCode != AlertRuleCodes.PendingReview), a => Assert.Equal(Alert.StatusOpen, a.Status));
        }
    }

    [Fact]
    public async Task Uyari_yapilandirilmis_kanala_teslimat_satiri_acar()
    {
        var (db, projectId, endpointId) = NewProject();
        using (db)
        {
            TestRunFixtures.SetSettings(db, projectId);
            using (var context = db.CreateContext())
            {
                context.ProjectAutomationSettings.Single(s => s.ProjectId == projectId).WebhookUrl = "https://1.1.1.1/hook";
                context.SaveChanges();
            }

            var clock = new TestClock();
            AddSuggestion(db, projectId, endpointId, clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(30));

            await using (var context = db.CreateContext())
            {
                await using var provider = TestServices.BuildProvider(context,
                    services => services.AddSingleton<IEventHandler<AlertRaised>, AlertRaisedHandler>(), clock);
                await new ReviewReminderService(context, provider.GetRequiredService<IEventPublisher>(), clock).EvaluateAsync();
                await provider.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();
            }

            using var check = db.CreateContext();
            var delivery = Assert.Single(check.NotificationDeliveries.Where(d => d.ProjectId == projectId));
            Assert.Equal(WebhookChannel.ChannelName, delivery.Channel);
            Assert.Contains("PENDING_REVIEW", delivery.PayloadJson);
        }
    }
}
