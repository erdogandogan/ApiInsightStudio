using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Automation;

/// <summary>
/// Analiz bitince kuralları değerlendirir. Uyarılar "kenar tetiklemeli" çalışır (bkz. <see cref="AlertReconciler"/>):
/// bir kural için zaten Açık bir uyarı varsa yenisi açılmaz (bildirim tekrarını önler); kural artık tutmuyorsa
/// Açık uyarı Çözüldü yapılır; sonra tekrar bozulursa yeni uyarı açılır. Yalnızca analiz kurallarını
/// (<see cref="AlertRuleCodes.AnalysisRules"/>) yönetir.
/// </summary>
public class AnalysisCompletedHandler : IEventHandler<AnalysisCompleted>
{
    private readonly AppDbContext _dbContext;
    private readonly AlertReconciler _reconciler;
    private readonly AlertRuleEvaluator _evaluator;

    public AnalysisCompletedHandler(AppDbContext dbContext, IEventPublisher events, AlertRuleEvaluator evaluator)
    {
        _dbContext = dbContext;
        _reconciler = new AlertReconciler(dbContext, events);
        _evaluator = evaluator;
    }

    public async Task HandleAsync(AnalysisCompleted @event, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId, cancellationToken)
            ?? new ProjectAutomationSettings { ProjectId = @event.ProjectId };

        // Kapalıysa hiçbir şey açılmaz ve mevcut uyarılar olduğu gibi kalır.
        if (!settings.Enabled)
            return;

        var analysis = await _dbContext.AnalysisResults
            .AsNoTracking()
            .Include(a => a.Warnings)
            .Where(a => a.ProjectId == @event.ProjectId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Proje olay işlenmeden silinmiş olabilir.
        if (analysis is null)
            return;

        var snapshot = new AnalysisSnapshot(
            analysis.Score,
            analysis.Warnings.Count(w => w.Type == "Security" && w.Message == WarningMessages.MissingAuthentication));

        var candidates = _evaluator.Evaluate(snapshot, settings);

        await _reconciler.ReconcileAsync(@event.ProjectId, candidates, AlertRuleCodes.AnalysisRules, cancellationToken);
    }
}
