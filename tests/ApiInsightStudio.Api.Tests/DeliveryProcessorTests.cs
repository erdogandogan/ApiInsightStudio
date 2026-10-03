using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Teslimat işleyicisinin gönderme, yeniden deneme, ölü olma ve kanal yalıtımı davranışları.</summary>
public class DeliveryProcessorTests
{
    private sealed class FakeChannel : INotificationChannel
    {
        private readonly Func<NotificationContext, DeliveryResult> _send;

        public string Name { get; }
        public int Calls { get; private set; }
        public NotificationContext? LastContext { get; private set; }

        public FakeChannel(string name, Func<NotificationContext, DeliveryResult> send)
        {
            Name = name;
            _send = send;
        }

        public Task<DeliveryResult> SendAsync(NotificationContext context, CancellationToken cancellationToken)
        {
            Calls++;
            LastContext = context;
            return Task.FromResult(_send(context));
        }
    }

    private readonly TestClock _clock = new();

    private DeliveryProcessor CreateProcessor(AppDbContext context, params INotificationChannel[] channels) =>
        new(context, channels, _clock, NullLogger<DeliveryProcessor>.Instance);

    private static AlertRaised Alert(int projectId, Guid? eventId = null) =>
        new(projectId, "LOW_SCORE", "Kalite skoru düşük.", "Medium", DateTime.UtcNow, eventId ?? Guid.NewGuid());

    private static int SeedDelivery(TestDb db, int projectId, string channel = "fake", Guid? eventId = null)
    {
        using var context = db.CreateContext();
        var alert = Alert(projectId, eventId);
        var delivery = new NotificationDelivery
        {
            ProjectId = projectId,
            EventId = alert.EventId,
            Channel = channel,
            PayloadJson = JsonSerializer.Serialize(alert)
        };
        context.NotificationDeliveries.Add(delivery);
        context.SaveChanges();
        return delivery.Id;
    }

    private static NotificationDelivery Load(TestDb db, int id)
    {
        using var context = db.CreateContext();
        return context.NotificationDeliveries.Single(d => d.Id == id);
    }

    private static async Task<int> ProcessAsync(TestDb db, Func<AppDbContext, DeliveryProcessor> make)
    {
        await using var context = db.CreateContext();
        return await make(context).ProcessDueAsync();
    }

    // ----- Başarı -----

    [Fact]
    public async Task Basarili_gonderim_Succeeded_yapar_ve_denemeyi_sayar()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok(200));

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusSucceeded, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(200, delivery.LastStatusCode);
        Assert.Null(delivery.LastError);
        Assert.Null(delivery.NextAttemptAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, delivery.CompletedAt);
    }

    [Fact]
    public async Task Kanala_proje_adi_uyari_ve_ayarlar_iletilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        using (var context = db.CreateContext())
        {
            context.ProjectAutomationSettings.Add(new ProjectAutomationSettings
            {
                ProjectId = projectId, WebhookUrl = "https://hooks.example.com/x"
            });
            context.SaveChanges();
        }
        SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok());

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        Assert.Equal("Test projesi", channel.LastContext!.ProjectName);
        Assert.Equal("LOW_SCORE", channel.LastContext.Alert.RuleCode);
        Assert.Equal("https://hooks.example.com/x", channel.LastContext.Settings.WebhookUrl);
    }

    [Fact]
    public async Task Basarili_teslimat_bir_daha_gonderilmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok());

        for (var i = 0; i < 3; i++)
        {
            await ProcessAsync(db, c => CreateProcessor(c, channel));
            _clock.Advance(TimeSpan.FromDays(1));
        }

        Assert.Equal(1, channel.Calls);
    }

    // ----- Geçici hata, geri çekilme -----

    [Fact]
    public async Task Gecici_hata_Pending_kalir_10_sn_sonraya_ertelenir_ve_hata_kaydedilir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Failed(503, "Alıcı 503 durum kodu döndürdü."));

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusPending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(503, delivery.LastStatusCode);
        Assert.Contains("503", delivery.LastError);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddSeconds(10), delivery.NextAttemptAt);
        Assert.Null(delivery.CompletedAt);
    }

    [Fact]
    public async Task Vakti_gelmeyen_teslimat_denenmez_gelince_denenir_ve_bekleme_katlanir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Failed(500, "hata"));

        await ProcessAsync(db, c => CreateProcessor(c, channel));
        Assert.Equal(1, channel.Calls);

        _clock.Advance(TimeSpan.FromSeconds(9));
        await ProcessAsync(db, c => CreateProcessor(c, channel));
        Assert.Equal(1, channel.Calls); // henüz vakit gelmedi

        _clock.Advance(TimeSpan.FromSeconds(1));
        await ProcessAsync(db, c => CreateProcessor(c, channel));
        Assert.Equal(2, channel.Calls);

        var delivery = Load(db, id);
        Assert.Equal(2, delivery.Attempts);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddSeconds(20), delivery.NextAttemptAt);
    }

    [Fact]
    public async Task Gecici_hata_sonra_duzelirse_teslimat_basarili_olur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var healthy = false;
        var channel = new FakeChannel("fake", _ => healthy ? DeliveryResult.Ok(200) : DeliveryResult.Failed(502, "hata"));

        await ProcessAsync(db, c => CreateProcessor(c, channel));
        healthy = true;
        _clock.Advance(TimeSpan.FromSeconds(10));
        await ProcessAsync(db, c => CreateProcessor(c, channel));

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusSucceeded, delivery.Status);
        Assert.Equal(2, delivery.Attempts);
        Assert.Null(delivery.LastError);
        Assert.Null(delivery.NextAttemptAt);
    }

    // ----- Ölü teslimat -----

    [Fact]
    public async Task Hak_bitince_teslimat_Dead_olur_ve_bir_daha_denenmez()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Failed(500, "hata"));

        for (var i = 0; i < RetryPolicy.MaxAttempts + 3; i++)
        {
            await ProcessAsync(db, c => CreateProcessor(c, channel));
            _clock.Advance(TimeSpan.FromDays(1));
        }

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusDead, delivery.Status);
        Assert.Equal(RetryPolicy.MaxAttempts, delivery.Attempts);
        Assert.Equal(RetryPolicy.MaxAttempts, channel.Calls);
        Assert.NotNull(delivery.CompletedAt);
        Assert.Null(delivery.NextAttemptAt);
    }

    [Fact]
    public async Task Kalici_hata_hic_yeniden_denenmeden_hemen_Dead_olur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => DeliveryResult.Permanent("Webhook adresi kaldırılmış."));

        await ProcessAsync(db, c => CreateProcessor(c, channel));
        _clock.Advance(TimeSpan.FromDays(1));
        await ProcessAsync(db, c => CreateProcessor(c, channel));

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusDead, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(1, channel.Calls);
        Assert.Equal("Webhook adresi kaldırılmış.", delivery.LastError);
    }

    [Fact]
    public async Task Bilinmeyen_kanal_Dead_olur()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId, channel: "slack");
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok());

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        Assert.Equal(NotificationDelivery.StatusDead, Load(db, id).Status);
        Assert.Equal(0, channel.Calls);
    }

    [Fact]
    public async Task Proje_silinmisse_teslimat_Dead_olur_ve_kanal_cagrilmaz()
    {
        using var db = new TestDb();
        var userId = db.SeedUser();
        var projectId = db.SeedProject(userId);
        var id = SeedDelivery(db, projectId);
        using (var context = db.CreateContext())
        {
            // Yabancı anahtar korumasını aşmadan "proje yok" durumunu üret: teslimat başka bir proje kimliğine bağlı
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF");
            context.Database.ExecuteSqlRaw("UPDATE NotificationDeliveries SET ProjectId = 99999");
        }
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok());

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        Assert.Equal(NotificationDelivery.StatusDead, Load(db, id).Status);
        Assert.Equal(0, channel.Calls);
    }

    [Fact]
    public async Task Kanal_beklenmedik_istisna_atarsa_gecici_hata_sayilir_ve_istisna_metni_sizmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var id = SeedDelivery(db, projectId);
        var channel = new FakeChannel("fake", _ => throw new InvalidOperationException("içeride TOKEN=abc123 vardı"));

        await ProcessAsync(db, c => CreateProcessor(c, channel));

        var delivery = Load(db, id);
        Assert.Equal(NotificationDelivery.StatusPending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Contains("InvalidOperationException", delivery.LastError);
        Assert.DoesNotContain("abc123", delivery.LastError);
    }

    // ----- Kanal yalıtımı ve toplu işleme -----

    [Fact]
    public async Task Bir_kanalin_basarisizligi_digerini_etkilemez_ve_basarili_kanala_mukerrer_gonderim_olmaz()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var eventId = Guid.NewGuid();
        var failingId = SeedDelivery(db, projectId, channel: "a", eventId: eventId);
        var okId = SeedDelivery(db, projectId, channel: "b", eventId: eventId);
        var a = new FakeChannel("a", _ => DeliveryResult.Failed(500, "hata"));
        var b = new FakeChannel("b", _ => DeliveryResult.Ok(200));

        await ProcessAsync(db, c => CreateProcessor(c, a, b));
        Assert.Equal(NotificationDelivery.StatusPending, Load(db, failingId).Status);
        Assert.Equal(NotificationDelivery.StatusSucceeded, Load(db, okId).Status);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await ProcessAsync(db, c => CreateProcessor(c, a, b));

        Assert.Equal(2, a.Calls); // başarısız olan yeniden denendi
        Assert.Equal(1, b.Calls); // başarılı olana ikinci kez gönderilmedi
    }

    [Fact]
    public async Task Birden_fazla_vakti_gelmis_teslimat_tek_turda_islenir()
    {
        using var db = new TestDb();
        var projectId = db.SeedProject(db.SeedUser());
        var ids = Enumerable.Range(0, 5).Select(_ => SeedDelivery(db, projectId)).ToList();
        var channel = new FakeChannel("fake", _ => DeliveryResult.Ok());

        var processed = await ProcessAsync(db, c => CreateProcessor(c, channel));

        Assert.Equal(5, processed);
        Assert.All(ids, id => Assert.Equal(NotificationDelivery.StatusSucceeded, Load(db, id).Status));
    }

    [Fact]
    public async Task Hic_teslimat_yoksa_sifir_doner()
    {
        using var db = new TestDb();

        Assert.Equal(0, await ProcessAsync(db, c => CreateProcessor(c, new FakeChannel("fake", _ => DeliveryResult.Ok()))));
    }
}
