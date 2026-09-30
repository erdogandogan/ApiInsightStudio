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
}
