using System.Net;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using ApiInsightStudio.Api.Services;
using ApiInsightStudio.Api.TestRunner;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Atılan tek bir istek (sahte hedef sunucunun gördüğü).</summary>
public sealed record RecordedRequest(string Method, string PathAndQuery, string? Authorization, string? Body, string? ContentType)
{
    public string Path => PathAndQuery.Contains('?') ? PathAndQuery[..PathAndQuery.IndexOf('?')] : PathAndQuery;
}

/// <summary>Hedef API'nin yerine geçen sahte HTTP işleyicisi: istekleri kaydeder, cevabı bir fonksiyon belirler.</summary>
public sealed class FakeTargetHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _respond;
    private readonly List<RecordedRequest> _requests = new();

    public FakeTargetHandler(Func<RecordedRequest, HttpResponseMessage> respond) => _respond = respond;

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.PathAndQuery,
            request.Headers.Authorization?.ToString(),
            body,
            request.Content?.Headers.ContentType?.MediaType);

        lock (_requests)
            _requests.Add(recorded);

        return _respond(recorded);
    }

    /// <summary>Tipik bir korumalı API: /items herkese açık, /items/{id} ve POST /items token ister.</summary>
    public static HttpResponseMessage TypicalApi(RecordedRequest r)
    {
        var hasAuth = r.Authorization is not null;
        if (r.Path == "/items" && r.Method == "GET")
            return new HttpResponseMessage(HttpStatusCode.OK);
        if (!hasAuth)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (r.Path == "/items" && r.Method == "POST")
            return new HttpResponseMessage(HttpStatusCode.BadRequest);   // boş gövde geçersiz
        return new HttpResponseMessage(HttpStatusCode.NotFound);         // /items/0 yok
    }
}

public sealed class StaticHandlerFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public StaticHandlerFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

/// <summary>Test koşucusu testleri için ortak kurulum yardımcıları.</summary>
public static class TestRunFixtures
{
    public const string Target = "https://api.example.test";
    public const string Token = "tok123-gizli";

    public static readonly WebhookSecretProtector Protector = new(new EphemeralDataProtectionProvider());

    /// <summary>Tipik 4 uç nokta: GET /items (açık), GET /items/{id}, POST /items, DELETE /items/{id} (kimlik ister).</summary>
    public static EndpointSpec[] TypicalEndpoints() => new[]
    {
        new EndpointSpec(Method: "GET", Path: "/items", AuthType: null),
        new EndpointSpec(Method: "GET", Path: "/items/{id}", AuthType: "Bearer"),
        new EndpointSpec(Method: "POST", Path: "/items", AuthType: "Bearer"),
        new EndpointSpec(Method: "DELETE", Path: "/items/{id}", AuthType: "Bearer")
    };

    /// <summary>Gerçek senaryo üreticisini çalıştırır: proje başına her uç nokta için standart senaryolar.</summary>
    public static async Task GenerateScenariosAsync(TestDb db, int projectId)
    {
        await using var context = db.CreateContext();
        await new TestGenerationService(context).GenerateTestsForProjectAsync(projectId);
    }

    public static void SetSettings(
        TestDb db, int projectId,
        string? target = Target, string? token = Token, bool allowMutating = false,
        int thresholdPercent = 20, bool enabled = true, WebhookSecretProtector? protector = null)
    {
        using var context = db.CreateContext();
        context.ProjectAutomationSettings.Add(new ProjectAutomationSettings
        {
            ProjectId = projectId,
            TargetBaseUrl = target,
            TargetBearerTokenProtected = token is null ? null : (protector ?? Protector).ProtectToken(token),
            AllowMutatingTests = allowMutating,
            TestFailureThresholdPercent = thresholdPercent,
            Enabled = enabled
        });
        context.SaveChanges();
    }

    public static int AddRun(TestDb db, int projectId, string status = TestRun.StatusPending, DateTime? startedAt = null, DateTime? createdAt = null)
    {
        using var context = db.CreateContext();
        var run = new TestRun
        {
            ProjectId = projectId,
            Status = status,
            StartedAt = startedAt,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        context.TestRuns.Add(run);
        context.SaveChanges();
        return run.Id;
    }

    public static TestRunExecutor CreateExecutor(
        AppDbContext context, HttpMessageHandler handler, TestClock? clock = null,
        WebhookSecretProtector? protector = null, params string[] allowedPrivateHosts) =>
        new(context,
            new StaticHandlerFactory(handler),
            protector ?? Protector,
            Options.Create(new NotificationOptions { AllowedPrivateHosts = allowedPrivateHosts }),
            new OutboxEventPublisher(context),
            clock ?? new TestClock());

    /// <summary>Koşuyu Running yapıp yürütür (işleyicinin yaptığı gibi) ve güncel halini yeni bir bağlamdan okur.</summary>
    public static async Task<TestRun> ExecuteAsync(
        TestDb db, int runId, HttpMessageHandler handler, TestClock? clock = null,
        WebhookSecretProtector? protector = null, params string[] allowedPrivateHosts)
    {
        await using (var context = db.CreateContext())
        {
            var run = context.TestRuns.Single(r => r.Id == runId);
            run.Status = TestRun.StatusRunning;
            run.StartedAt = DateTime.UtcNow;
            context.SaveChanges();

            await CreateExecutor(context, handler, clock, protector, allowedPrivateHosts).ExecuteAsync(run, CancellationToken.None);
        }

        return Load(db, runId);
    }

    public static TestRun Load(TestDb db, int runId)
    {
        using var context = db.CreateContext();
        return context.TestRuns.Include(r => r.Results).Single(r => r.Id == runId);
    }
}
