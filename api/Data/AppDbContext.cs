using Microsoft.EntityFrameworkCore;
using ApiInsightStudio.Api.Models;
using EndpointModel = ApiInsightStudio.Api.Models.Endpoint;

namespace ApiInsightStudio.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Project> Projects { get; set; } = null!;

    public DbSet<EndpointModel> Endpoints { get; set; } = null!;

    public DbSet<Response> Responses { get; set; } = null!;

    public DbSet<AnalysisResult> AnalysisResults { get; set; } = null!;

    public DbSet<Warning> Warnings { get; set; } = null!;

    public DbSet<TestScenario> TestScenarios { get; set; } = null!;

    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    public DbSet<ProjectAutomationSettings> ProjectAutomationSettings { get; set; } = null!;

    public DbSet<Alert> Alerts { get; set; } = null!;

    public DbSet<NotificationDelivery> NotificationDeliveries { get; set; } = null!;

    public DbSet<TestRun> TestRuns { get; set; } = null!;

    public DbSet<TestRunResult> TestRunResults { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Proje başına en fazla bir ayar satırı
        modelBuilder.Entity<ProjectAutomationSettings>()
            .HasIndex(settings => settings.ProjectId)
            .IsUnique();

        // "Bu proje ve kural için Açık uyarı var mı?" sorgusu için
        modelBuilder.Entity<Alert>()
            .HasIndex(alert => new { alert.ProjectId, alert.RuleCode, alert.Status });

        // Bekleyen mesajları hızlı bulmak için
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(message => new { message.ProcessedAt, message.Id });

        // Aynı olay ve kanal için en fazla bir teslimat satırı (idempotency)
        modelBuilder.Entity<NotificationDelivery>()
            .HasIndex(delivery => new { delivery.EventId, delivery.Channel })
            .IsUnique();

        // Zamanı gelmiş teslimatları hızlı bulmak için
        modelBuilder.Entity<NotificationDelivery>()
            .HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAt });

        // Bir projede aynı anda en fazla bir aktif (Pending/Running) koşu: iki eşzamanlı istek yarışsa bile veritabanı reddeder.
        modelBuilder.Entity<TestRun>()
            .HasIndex(run => run.ProjectId)
            .IsUnique()
            .HasFilter("[Status] IN ('Pending', 'Running')")
            .HasDatabaseName("UX_TestRuns_ProjectId_Active");

        // Bir projenin koşularını hızlı bulmak için
        modelBuilder.Entity<TestRun>()
            .HasIndex(run => new { run.ProjectId, run.Status });
        modelBuilder.Entity<TestRun>()
            .HasIndex(run => new { run.Status, run.CreatedAt });
    }
}
