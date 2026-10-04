using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Automation;

/// <summary>
/// Test koşusu bitince başarısızlık oranı kuralını değerlendirir (<see cref="AlertRuleCodes.TestFailures"/>).
/// Kenar tetiklemelidir (bkz. <see cref="AlertReconciler"/>) ve yalnızca kendi kuralını yönetir.
/// Hiç test çalışmadıysa (hepsi atlandı) değerlendirme yapılmaz: mevcut uyarı olduğu gibi kalır.
/// </summary>
public class TestRunCompletedHandler : IEventHandler<TestRunCompleted>
{
    private readonly AppDbContext _dbContext;
    private readonly AlertReconciler _reconciler;
    private readonly AlertRuleEvaluator _evaluator;

    public TestRunCompletedHandler(AppDbContext dbContext, IEventPublisher events, AlertRuleEvaluator evaluator)
    {
        _dbContext = dbContext;
        _reconciler = new AlertReconciler(dbContext, events);
        _evaluator = evaluator;
    }

    public async Task HandleAsync(TestRunCompleted @event, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId, cancellationToken)
            ?? new ProjectAutomationSettings { ProjectId = @event.ProjectId };

        // Kapalıysa hiçbir şey açılmaz ve mevcut uyarılar olduğu gibi kalır.
        if (!settings.Enabled)
            return;

        // Olaydaki sayılar yerine veritabanındaki koşu esas alınır; proje/koşu silinmiş olabilir.
        var run = await _dbContext.TestRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == @event.RunId && r.ProjectId == @event.ProjectId, cancellationToken);
        if (run is null || run.Status != TestRun.StatusCompleted)
            return;

        var snapshot = new TestRunSnapshot(run.Passed, run.Failed, run.Skipped);

        // Hiçbir test çalışmadıysa durum hakkında bilgi yok: ne uyarı aç ne de çöz.
        if (snapshot.Executed == 0)
            return;

        var candidates = _evaluator.EvaluateTestRun(snapshot, settings);
        await _reconciler.ReconcileAsync(@event.ProjectId, candidates, AlertRuleCodes.TestRunRules, cancellationToken);
    }
}
