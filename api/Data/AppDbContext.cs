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
    }
}
