using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using EndpointModel = ApiInsightStudio.Api.Models.Endpoint;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Her test için ayrı, bellekte çalışan bir SQLite veritabanı kurar.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public DbContextOptions<AppDbContext> Options { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateContext() => new(Options);

    /// <summary>Bir kullanıcı ekler ve kimliğini döndürür.</summary>
    public int SeedUser(string email = "user@example.com")
    {
        using var context = CreateContext();
        var user = new User { Name = "Test", Email = email, PasswordHash = "x" };
        context.Users.Add(user);
        context.SaveChanges();
        return user.Id;
    }

    /// <summary>Verilen endpoint tanımlarıyla bir proje ekler ve proje kimliğini döndürür.</summary>
    public int SeedProject(int userId, params EndpointSpec[] endpoints)
    {
        using var context = CreateContext();
        var project = new Project { Name = "Test projesi", UserId = userId };

        foreach (var spec in endpoints)
        {
            var endpoint = new EndpointModel
            {
                Project = project,
                Method = spec.Method,
                Path = spec.Path,
                Summary = spec.Summary,
                AuthType = spec.AuthType
            };

            foreach (var statusCode in spec.StatusCodes)
                endpoint.Responses.Add(new Response { StatusCode = statusCode, Description = "d" });

            context.Endpoints.Add(endpoint);
        }

        context.Projects.Add(project);
        context.SaveChanges();
        return project.Id;
    }

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Varsayılanlar "temiz" bir endpoint verir (açıklama var, hata kodu var, kimlik doğrulama var);
/// testler yalnızca denedikleri kuralı bozar.
/// </summary>
public sealed record EndpointSpec(
    string Method = "GET",
    string Path = "/items",
    string Summary = "Açıklama var",
    string[]? StatusCodesOrNull = null,
    string? AuthType = "Bearer")
{
    public string[] StatusCodes => StatusCodesOrNull ?? new[] { "200", "400" };
}
