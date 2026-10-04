using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Automation;

/// <summary>
/// Bir olay işleyicisinin bulduğu uyarı adaylarını veritabanındaki uyarılarla uzlaştırır (kenar tetikleme):
/// <list type="bullet">
/// <item>Aday var, Açık uyarı yok → yeni uyarı ve <see cref="AlertRaised"/> olayı.</item>
/// <item>Aday var, Açık uyarı var → bildirim yok, yalnızca güncel metin/önem yansıtılır.</item>
/// <item>Açık uyarı var, aday yok → uyarı Çözüldü olur.</item>
/// </list>
/// Yalnızca <paramref name="managedRuleCodes"/> içindeki kurallara dokunulur: her işleyici kendi kurallarını yönetir,
/// başka bir işleyicinin açtığı uyarıyı (örn. analiz işleyicisi test koşusu uyarısını) yanlışlıkla çözmez.
/// </summary>
public class AlertReconciler
{
    private readonly AppDbContext _dbContext;
    private readonly IEventPublisher _events;

    public AlertReconciler(AppDbContext dbContext, IEventPublisher events)
    {
        _dbContext = dbContext;
        _events = events;
    }

    public async Task ReconcileAsync(
        int projectId,
        IReadOnlyCollection<AlertCandidate> candidates,
        IReadOnlyCollection<string> managedRuleCodes,
        CancellationToken cancellationToken = default)
    {
        var openAlerts = await _dbContext.Alerts
            .Where(a => a.ProjectId == projectId
                        && a.Status == Alert.StatusOpen
                        && managedRuleCodes.Contains(a.RuleCode))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var candidate in candidates)
        {
            if (!managedRuleCodes.Contains(candidate.RuleCode))
                throw new InvalidOperationException($"'{candidate.RuleCode}' bu işleyicinin yönettiği kurallar arasında değil.");

            var existing = openAlerts.FirstOrDefault(a => a.RuleCode == candidate.RuleCode);
            if (existing is null)
            {
                _dbContext.Alerts.Add(new Alert
                {
                    ProjectId = projectId,
                    RuleCode = candidate.RuleCode,
                    Message = candidate.Message,
                    Severity = candidate.Severity,
                    Status = Alert.StatusOpen,
                    CreatedAt = now
                });

                _events.Publish(new AlertRaised(
                    projectId, candidate.RuleCode, candidate.Message, candidate.Severity, now, Guid.NewGuid()));
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
