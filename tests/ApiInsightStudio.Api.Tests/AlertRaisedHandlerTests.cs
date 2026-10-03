using System.Text.Json;
using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Uyarı olayı → teslimat satırı zinciri: yalnızca yapılandırılmış kanallar için, tekrarsız.</summary>
public class AlertRaisedHandlerTests
{
    private static AlertRaised Alert(int projectId, Guid? eventId = null) =>
        new(projectId, "LOW_SCORE", "Kalite skoru düşük.", "Medium", DateTime.UtcNow, eventId ?? Guid.NewGuid());

    private static void SetWebhook(TestDb db, int projectId, string? url = "https://hooks.example.com/alerts")
    {
        using var context = db.CreateContext();
        context.ProjectAutomationSettings.Add(new ProjectAutomationSettings { ProjectId = projectId, WebhookUrl = url });
        context.SaveChanges();
    }

    private static List<NotificationDelivery> Deliveries(TestDb db, int projectId)
    {
        using var context = db.CreateContext();
        return context.NotificationDeliveries.Where(d => d.ProjectId == projectId).OrderBy(d => d.Id).ToList();
    }

    private static async Task HandleAsync(TestDb db, AlertRaised alert)
    {
        await using var context = db.CreateContext();
        var handler = new AlertRaisedHandler(context, TimeProvider.System);
        await handler.HandleAsync(alert);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Webhook_adresi_varsa_bir_Pending_teslimat_acilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SetWebhook(db, projectId);
        var alert = Alert(projectId);

        await HandleAsync(db, alert);

        var delivery = Assert.Single(Deliveries(db, projectId));
        Assert.Equal("webhook", delivery.Channel);
        Assert.Equal(NotificationDelivery.StatusPending, delivery.Status);
        Assert.Equal(0, delivery.Attempts);
        Assert.Equal(alert.EventId, delivery.EventId);
        Assert.Contains("LOW_SCORE", delivery.PayloadJson);
    }

    [Fact]
    public async Task Teslimat_icerigi_olayin_kopyasidir_ve_geri_okunabilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SetWebhook(db, projectId);
        var alert = Alert(projectId);

        await HandleAsync(db, alert);

        var restored = JsonSerializer.Deserialize<AlertRaised>(Deliveries(db, projectId).Single().PayloadJson);
        Assert.Equal(alert.EventId, restored!.EventId);
        Assert.Equal(alert.Message, restored.Message);
        Assert.Equal(alert.ProjectId, restored.ProjectId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Webhook_adresi_yoksa_teslimat_acilmaz(string? url)
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SetWebhook(db, projectId, url);

        await HandleAsync(db, Alert(projectId));

        Assert.Empty(Deliveries(db, projectId));
    }

    [Fact]
    public async Task Ayar_satiri_yoksa_teslimat_acilmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());

        await HandleAsync(db, Alert(projectId));

        Assert.Empty(Deliveries(db, projectId));
    }

    [Fact]
    public async Task Ayni_olay_iki_kez_islenirse_tek_teslimat_kalir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SetWebhook(db, projectId);
        var alert = Alert(projectId);

        await HandleAsync(db, alert);
        await HandleAsync(db, alert);

        Assert.Single(Deliveries(db, projectId));
    }

    [Fact]
    public async Task Farkli_olaylar_ayri_teslimat_alir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SetWebhook(db, projectId);

        await HandleAsync(db, Alert(projectId));
        await HandleAsync(db, Alert(projectId));

        Assert.Equal(2, Deliveries(db, projectId).Count);
    }

    // ----- Tüm zincir: analiz → uyarı → olay → teslimat -----

    private static async Task AnalyzeWithFullPipelineAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        using var provider = TestServices.BuildProvider(context, services =>
        {
            services.AddSingleton<IEventHandler<AnalysisCompleted>, AnalysisCompletedHandler>();
            services.AddSingleton<IEventHandler<AlertRaised>>(sp =>
                new AlertRaisedHandler(sp.GetRequiredService<AppDbContext>(), TimeProvider.System));
        });
        await TestServices.CreateAnalysisService(provider).AnalyzeProjectAsync(projectId);
    }

    private static EndpointSpec[] LowScoreEndpoints() => new[]
    {
        new EndpointSpec(Path: "/a", Summary: "", StatusCodesOrNull: new[] { "200" }),
        new EndpointSpec(Path: "/b", Summary: "", StatusCodesOrNull: new[] { "200" })
    };

    [Fact]
    public async Task Analiz_tek_cagrida_uyariyi_olayi_ve_teslimati_uretir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());
        SetWebhook(db, projectId);

        await AnalyzeWithFullPipelineAsync(db, projectId);

        using var context = db.CreateContext();
        var alert = Assert.Single(context.Alerts.Where(a => a.ProjectId == projectId));
        Assert.Equal("LOW_SCORE", alert.RuleCode);
        var raised = Assert.Single(context.OutboxMessages.Where(m => m.Type == nameof(AlertRaised)));
        Assert.NotNull(raised.ProcessedAt); // olay zincirde işlendi
        var delivery = Assert.Single(Deliveries(db, projectId));
        Assert.Equal(NotificationDelivery.StatusPending, delivery.Status);
    }

    [Fact]
    public async Task Tekrarlanan_analiz_ayni_uyari_icin_ikinci_teslimat_uretmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());
        SetWebhook(db, projectId);

        await AnalyzeWithFullPipelineAsync(db, projectId);
        await AnalyzeWithFullPipelineAsync(db, projectId);
        await AnalyzeWithFullPipelineAsync(db, projectId);

        Assert.Single(Deliveries(db, projectId));
    }

    [Fact]
    public async Task Webhook_olmayan_projede_uyari_acilir_ama_teslimat_acilmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());

        await AnalyzeWithFullPipelineAsync(db, projectId);

        using var context = db.CreateContext();
        Assert.Single(context.Alerts.Where(a => a.ProjectId == projectId));
        Assert.Empty(Deliveries(db, projectId));
    }

    [Fact]
    public async Task Uyari_cozulup_tekrar_bozulursa_yeni_teslimat_acilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser(), LowScoreEndpoints());
        SetWebhook(db, projectId);
        await AnalyzeWithFullPipelineAsync(db, projectId);

        using (var context = db.CreateContext())
        {
            foreach (var endpoint in context.Endpoints.Include(e => e.Responses).Where(e => e.ProjectId == projectId))
            {
                endpoint.Summary = "Açıklama";
                endpoint.Responses.Add(new Response { StatusCode = "400", Description = "d" });
            }
            context.SaveChanges();
        }
        await AnalyzeWithFullPipelineAsync(db, projectId); // çözüldü

        using (var context = db.CreateContext())
        {
            foreach (var endpoint in context.Endpoints.Where(e => e.ProjectId == projectId))
                endpoint.Summary = "";
            context.Responses.RemoveRange(context.Responses.Where(r => r.StatusCode == "400"));
            context.SaveChanges();
        }
        await AnalyzeWithFullPipelineAsync(db, projectId); // tekrar bozuldu

        Assert.Equal(2, Deliveries(db, projectId).Count);
    }
}
