using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.TestRunner;

/// <summary>
/// Bir test koşusunu çalıştırır: senaryoları planlar (<see cref="TestRunPlanner"/>), çalıştırılabilir olanları hedef
/// API'ye güvenli HTTP istemcisiyle (SSRF korumalı, yönlendirme yok) atar, sonuçları kaydeder ve
/// <see cref="TestRunCompleted"/> olayını yayınlar. Yanıt gövdeleri okunmaz/saklanmaz; yalnızca durum kodu.
/// </summary>
public class TestRunExecutor
{
    public const string HttpClientName = "testrunner";

    /// <summary>Aynı anda en fazla bu kadar istek uçuşta olur.</summary>
    public const int MaxConcurrency = 3;

    /// <summary>Üst üste bu kadar bağlantı hatasından sonra kalan istekler atılmadan atlanır.</summary>
    public const int ConsecutiveConnectionFailureLimit = 5;

    private readonly AppDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookSecretProtector _protector;
    private readonly NotificationOptions _options;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _time;

    public TestRunExecutor(
        AppDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        WebhookSecretProtector protector,
        IOptions<NotificationOptions> options,
        IEventPublisher events,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _options = options.Value;
        _events = events;
        _time = time;
    }

    /// <summary>
    /// Çalışır durumdaki (Running) koşuyu yürütür ve Completed ya da Failed olarak kaydeder.
    /// Beklenmeyen istisnaları çağırana (işleyiciye) bırakır.
    /// </summary>
    public async Task ExecuteAsync(TestRun run, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == run.ProjectId, cancellationToken);

        if (string.IsNullOrWhiteSpace(settings?.TargetBaseUrl))
        {
            await FailAsync(run, "Hedef adres (targetBaseUrl) ayarlı değil.", cancellationToken);
            return;
        }

        var check = TargetUrl.Validate(settings.TargetBaseUrl, _options.AllowedPrivateHosts);
        if (!check.Ok)
        {
            await FailAsync(run, $"Hedef adres geçerli değil: {check.Error}", cancellationToken);
            return;
        }

        var baseUri = check.Uri!;
        run.TargetHost = baseUri.Authority;

        string? token = null;
        if (!string.IsNullOrWhiteSpace(settings.TargetBearerTokenProtected))
        {
            try
            {
                token = _protector.UnprotectToken(settings.TargetBearerTokenProtected);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                await FailAsync(run, "Hedef token'ı çözülemedi (anahtar halkası değişmiş olabilir); token'ı yeniden tanımlayın.", cancellationToken);
                return;
            }
        }

        var project = await _dbContext.Projects
            .AsNoTracking()
            .Where(p => p.Id == run.ProjectId)
            .Select(p => new { p.OpenApiContent })
            .FirstOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            await FailAsync(run, "Proje bulunamadı.", cancellationToken);
            return;
        }

        var scenarios = await _dbContext.TestScenarios
            .AsNoTracking()
            .Where(s => s.Endpoint.ProjectId == run.ProjectId)
            .OrderBy(s => s.Id)
            .Select(s => new ScenarioInput(s.EndpointId, s.Title, s.Method, s.Path, s.ExpectedStatusCode, s.Endpoint.AuthType))
            .ToListAsync(cancellationToken);
        if (scenarios.Count == 0)
        {
            await FailAsync(run, "Çalıştırılacak test senaryosu yok.", cancellationToken);
            return;
        }

        var filler = OpenApiPathParameterFiller.FromOpenApi(project.OpenApiContent);
        var plan = TestRunPlanner.Plan(scenarios, settings.AllowMutatingTests, hasToken: token is not null, filler.Fill);

        var results = await RunPlanAsync(plan, baseUri, token, cancellationToken);

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var result in results)
            run.Results.Add(result);

        run.Total = results.Count;
        run.Passed = results.Count(r => r.Outcome == TestRunResult.OutcomePassed);
        run.Failed = results.Count(r => r.Outcome == TestRunResult.OutcomeFailed);
        run.Skipped = results.Count(r => r.Outcome == TestRunResult.OutcomeSkipped);
        run.Status = TestRun.StatusCompleted;
        run.CompletedAt = now;

        // Olay, sonuçlarla aynı işlemde outbox'a yazılır (bkz. OutboxDispatcher).
        _events.Publish(new TestRunCompleted(run.ProjectId, run.Id, run.Passed, run.Failed, run.Skipped, now));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task FailAsync(TestRun run, string error, CancellationToken cancellationToken)
    {
        run.Status = TestRun.StatusFailed;
        run.CompletedAt = _time.GetUtcNow().UtcDateTime;
        run.Error = error;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<TestRunResult>> RunPlanAsync(
        IReadOnlyList<PlanItem> plan, Uri baseUri, string? token, CancellationToken cancellationToken)
    {
        var results = new TestRunResult[plan.Count];
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var consecutiveFailures = 0;
        var aborted = 0; // 0/1: Interlocked ile okunur/yazılır

        async Task RunOneAsync(int index)
        {
            var item = plan[index];
            if (!item.IsRunnable)
            {
                results[index] = SkippedResult(item, item.SkipReason!);
                return;
            }

            await gate.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref aborted) == 1)
                {
                    results[index] = SkippedResult(item,
                        $"Hedefe ulaşılamıyor ({ConsecutiveConnectionFailureLimit} ardışık bağlantı hatası); kalan istekler atılmadı.");
                    return;
                }

                var result = await SendAsync(client, item, baseUri, token, cancellationToken);
                results[index] = result;

                // Yanıt alındıysa sayaç sıfırlanır; yalnızca bağlantı düzeyinde hatalar (yanıt yok) sayılır.
                if (result.ActualStatusCode is not null)
                {
                    Interlocked.Exchange(ref consecutiveFailures, 0);
                }
                else if (Interlocked.Increment(ref consecutiveFailures) >= ConsecutiveConnectionFailureLimit)
                {
                    Interlocked.Exchange(ref aborted, 1);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, plan.Count).Select(RunOneAsync));
        return results.ToList();
    }

    private async Task<TestRunResult> SendAsync(
        HttpClient client, PlanItem item, Uri baseUri, string? token, CancellationToken cancellationToken)
    {
        var call = item.Call!;
        var result = BaseResult(item.Scenario);
        result.RequestPath = call.RequestPath;

        var uri = TargetUrl.Combine(baseUri, call.RequestPath);
        if (uri is null)
        {
            result.Outcome = TestRunResult.OutcomeSkipped;
            result.Reason = "İstek adresi hedef ana bilgisayarın dışına çıkıyor; atlandı.";
            return result;
        }

        using var request = new HttpRequestMessage(new HttpMethod(call.Method), uri);
        request.Headers.TryAddWithoutValidation("User-Agent", "ApiInsightStudio-TestRunner/1.0");
        if (call.SendToken && token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (call.SendBody)
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();

            var actual = (int)response.StatusCode;
            result.ActualStatusCode = actual;
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            if (TestRunPlanner.IsAccepted(item.Scenario.ExpectedStatusCode, actual))
            {
                result.Outcome = TestRunResult.OutcomePassed;
            }
            else
            {
                result.Outcome = TestRunResult.OutcomeFailed;
                result.Reason = $"Beklenen {TestRunPlanner.DescribeExpectation(item.Scenario.ExpectedStatusCode)}, gelen {actual}.";
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Outcome = TestRunResult.OutcomeFailed;
            result.Reason = "Zaman aşımı: hedef 10 saniyede cevap vermedi.";
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Outcome = TestRunResult.OutcomeFailed;
            // İstisna metni adres içerebilir; yalnızca tür kaydedilir.
            result.Reason = FindUnsafeTarget(ex)
                ? "Hedef adres güvenli değil (özel/yerel ağ); bağlantı engellendi."
                : $"Bağlantı hatası: {ex.GetBaseException().GetType().Name}.";
        }

        return result;
    }

    private static bool FindUnsafeTarget(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnsafeTargetException)
                return true;
        }

        return false;
    }

    private static TestRunResult SkippedResult(PlanItem item, string reason)
    {
        var result = BaseResult(item.Scenario);
        result.Outcome = TestRunResult.OutcomeSkipped;
        result.Reason = reason;
        return result;
    }

    private static TestRunResult BaseResult(ScenarioInput scenario) => new()
    {
        EndpointId = scenario.EndpointId,
        Title = scenario.Title,
        Method = scenario.Method,
        Path = scenario.Path,
        ExpectedStatusCode = scenario.ExpectedStatusCode
    };
}
