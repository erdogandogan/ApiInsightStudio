using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Automation;

/// <summary>
/// Analiz bitince kuralları değerlendirir. Uyarılar "kenar tetiklemeli" çalışır: bir kural için zaten
/// Açık bir uyarı varsa yenisi açılmaz (bildirim tekrarını önler); kural artık tutmuyorsa Açık uyarı
/// Çözüldü yapılır; sonra tekrar bozulursa yeni uyarı açılır.
/// </summary>
public class AnalysisCompletedHandler : IEventHandler<AnalysisCompleted>
{
    private readonly AppDbContext _dbContext;
    private readonly IEventPublisher _events;
    private readonly AlertRuleEvaluator _evaluator;

    public AnalysisCompletedHandler(AppDbContext dbContext, IEventPublisher events, AlertRuleEvaluator evaluator)
    {
        _dbContext = dbContext;
        _events = events;
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

        var openAlerts = await _dbContext.Alerts
            .Where(a => a.ProjectId == @event.ProjectId && a.Status == Alert.StatusOpen)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var candidate in candidates)
        {
            var existing = openAlerts.FirstOrDefault(a => a.RuleCode == candidate.RuleCode);
            if (existing is null)
            {
                _dbContext.Alerts.Add(new Alert
                {
                    ProjectId = @event.ProjectId,
                    RuleCode = candidate.RuleCode,
                    Message = candidate.Message,
                    Severity = candidate.Severity,
                    Status = Alert.StatusOpen,
                    CreatedAt = now
                });

                _events.Publish(new AlertRaised(
                    @event.ProjectId, candidate.RuleCode, candidate.Message, candidate.Severity, now, Guid.NewGuid()));
            }
            else
            {
                // Açık uyarı sürüyor: yeni bildirim yok, yalnızca güncel sayı/metin yansıtılır.
                existing.Message = candidate.Message;
                existing.Severity = candidate.Severity;
            }
        }

        var stillTriggered = candidates.Select(c => c.RuleCode).ToHashSet();
        foreach (var alert in openAlerts.Where(a => !stillTriggered.Contains(a.RuleCode)))
        {
            alert.Status = Alert.StatusResolved;
            alert.ResolvedAt = now;
        }
    }
}
