using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Gerçek HTTP hattını (JWT, yetkilendirme, controller'lar) SQLite in-memory ve sahte Ollama ile ayağa kaldırır.
/// "Testing" ortamı kullanılır; böylece Development'a özgü user-secrets yüklenmez ve gerçek anahtarlara dokunulmaz.
/// JWT anahtarı her çalıştırmada rastgele üretilir.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public const string OllamaReplyText = "Ürünleri listeler.";

    /// <summary>Ollama'ya giden isteklerin sayısı (yetkisiz isteklerin modele hiç ulaşmadığını doğrulamak için).</summary>
    public int OllamaCalls { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("Jwt:Issuer", "test-issuer");
        builder.UseSetting("Jwt:Audience", "test-audience");
        // Arka plan işçisi testlerde kapalı: işleme, testlerin kontrolünde (doğrudan çağrılarak) yapılır.
        builder.UseSetting("Notifications:WorkerEnabled", "false");

        builder.ConfigureServices(services =>
        {
            // Webhook sırları test sırasında geçici anahtarla şifrelenir; kullanıcı profiline anahtar yazılmaz.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            services.AddHttpClient<AiService>()
                .ConfigurePrimaryHttpMessageHandler(() => new FakeOllamaHandler(this));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>Kayıt olur, giriş yapar ve Bearer token'ı takılmış bir istemci döndürür.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = CreateClient();
        const string password = "Test-Pass-123";

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { name = "Test", email, password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    private sealed record LoginResponse(string Token);

    private sealed class FakeOllamaHandler : HttpMessageHandler
    {
        private readonly ApiFactory _owner;

        public FakeOllamaHandler(ApiFactory owner) => _owner = owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _owner.OllamaCalls++;
            var json = System.Text.Json.JsonSerializer.Serialize(new { response = OllamaReplyText });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
