using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.TestRunner;

/// <summary>
/// Kuyruktaki (Pending) test koşularını sırayla, birer birer çalıştırır. Arka plan işçisi ve testler çağırır.
/// Yarıda kalmış (Running) koşuları kurtarır, son <see cref="RunsToKeepPerProject"/> koşuyu saklar ve koşu
/// bitince olayı hemen işletir (uyarı + bildirim gecikmesin).
/// </summary>
public class TestRunProcessor
{
    /// <summary>Her projede saklanan en yeni koşu sayısı; eskileri (sonuçlarıyla) silinir.</summary>
    public const int RunsToKeepPerProject = 20;

    /// <summary>Running durumunda bu süreyi aşan koşu yarıda kalmış sayılır (uygulama yeniden başlamış olabilir).</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    // Tek sunucu varsayımı: aynı koşunun iki kez çalıştırılmaması için süreç genelinde tek işleyici çalışır.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _dbContext;
    private readonly TestRunExecutor _executor;
    private readonly OutboxDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger<TestRunProcessor> _logger;

    public TestRunProcessor(
        AppDbContext dbContext,
        TestRunExecutor executor,
        OutboxDispatcher dispatcher,
        TimeProvider time,
        ILogger<TestRunProcessor> logger)
    {
        _dbContext = dbContext;
        _executor = executor;
        _dispatcher = dispatcher;
        _time = time;
        _logger = logger;
    }

    /// <summary>Çalıştırılan koşu sayısını döndürür (0 veya 1). İstisna fırlatmaz.</summary>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        try
        {
            await RecoverStuckRunsAsync(cancellationToken);

            var run = await _dbContext.TestRuns
                .Where(r => r.Status == TestRun.StatusPending)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (run is null)
                return 0;

            run.Status = TestRun.StatusRunning;
            run.StartedAt = _time.GetUtcNow().UtcDateTime;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var projectId = run.ProjectId;
            var runId = run.Id;
            try
            {
                await _executor.ExecuteAsync(run, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Uygulama kapanıyor: koşu Running kalır, bir sonraki açılışta "yarıda kaldı" olarak kurtarılır.
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError("Test koşusu {RunId} beklenmeyen hatayla başarısız oldu: {ExceptionType}.", runId, ex.GetType().Name);

                // Yarım kalan değişiklikler atılır; yalnızca durum ve (hassas olmayan) hata kaydedilir.
                _dbContext.ChangeTracker.Clear();
                var failedAt = _time.GetUtcNow().UtcDateTime;
                var error = $"Beklenmeyen hata: {ex.GetType().Name}.";
                await _dbContext.TestRuns
                    .Where(r => r.Id == runId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, TestRun.StatusFailed)
                        .SetProperty(r => r.CompletedAt, failedAt)
                        .SetProperty(r => r.Error, error), CancellationToken.None);
            }

            await TrimOldRunsAsync(projectId, cancellationToken);

            // TestRunCompleted → uyarı → teslimat zinciri hemen işlensin
            await _dispatcher.DispatchPendingAsync(cancellationToken);
            return 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError("Test koşuları işlenirken beklenmeyen hata: {ExceptionType}.", ex.GetType().Name);
            return 0;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task RecoverStuckRunsAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var threshold = now - StuckAfter;

        await _dbContext.TestRuns
            .Where(r => r.Status == TestRun.StatusRunning && r.StartedAt != null && r.StartedAt < threshold)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, TestRun.StatusFailed)
                .SetProperty(r => r.CompletedAt, now)
                .SetProperty(r => r.Error, "Koşu yarıda kesildi (uygulama yeniden başlamış olabilir)."), cancellationToken);
    }

    private async Task TrimOldRunsAsync(int projectId, CancellationToken cancellationToken)
    {
        var obsoleteIds = await _dbContext.TestRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.Id)
            .Skip(RunsToKeepPerProject)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (obsoleteIds.Count == 0)
            return;

        // Sonuçlar, yabancı anahtardaki cascade ile birlikte silinir.
        await _dbContext.TestRuns
            .Where(r => obsoleteIds.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
