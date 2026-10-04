using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Automation;

/// <summary>
/// 24 saatten uzun süredir onay bekleyen AI önerisi olan projeler için <see cref="AlertRuleCodes.PendingReview"/>
/// uyarısını yönetir. Olaya bağlı değil zamana bağlı olduğu için arka plan işçisi her turda çağırır. Kenar tetiklemelidir
/// (bkz. <see cref="AlertReconciler"/>): bekleyen öneri sürdükçe tekrar bildirim gitmez; hepsi incelenince uyarı çözülür.
/// </summary>
public class ReviewReminderService
{
    /// <summary>Bir önerinin "gecikmiş" sayılması için bekleme süresi.</summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromHours(24);

    private readonly AppDbContext _dbContext;
    private readonly AlertReconciler _reconciler;
    private readonly TimeProvider _time;

    public ReviewReminderService(AppDbContext dbContext, IEventPublisher events, TimeProvider time)
    {
        _dbContext = dbContext;
        _reconciler = new AlertReconciler(dbContext, events);
        _time = time;
    }

    /// <summary>Tüm projeleri değerlendirir; açılan veya çözülen uyarı sayısını döndürmez, yalnızca kaydeder.</summary>
    public async Task EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - OverdueAfter;

        var overdue = await _dbContext.AiSuggestions
            .AsNoTracking()
            .Where(s => s.Status == AiSuggestion.StatusPending && s.CreatedAt < cutoff)
            .GroupBy(s => s.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);

        // Açık uyarısı olup artık gecikmiş önerisi kalmayanlar çözülmek üzere de değerlendirilir.
        var withOpenAlert = await _dbContext.Alerts
            .AsNoTracking()
            .Where(a => a.RuleCode == AlertRuleCodes.PendingReview && a.Status == Alert.StatusOpen)
            .Select(a => a.ProjectId)
            .ToListAsync(cancellationToken);

        var projectIds = overdue.Keys.Union(withOpenAlert).ToList();
        if (projectIds.Count == 0)
            return;

        var disabled = (await _dbContext.ProjectAutomationSettings
                .AsNoTracking()
                .Where(s => projectIds.Contains(s.ProjectId) && !s.Enabled)
                .Select(s => s.ProjectId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var projectId in projectIds)
        {
            // Kapalı projede hiçbir şey açılmaz ve mevcut uyarılar olduğu gibi kalır (diğer işleyicilerle aynı kural).
            if (disabled.Contains(projectId))
                continue;

            var candidates = overdue.TryGetValue(projectId, out var count)
                ? new[]
                {
                    new AlertCandidate(
                        AlertRuleCodes.PendingReview,
                        $"{count} AI önerisi {(int)OverdueAfter.TotalHours} saatten uzun süredir onayınızı bekliyor.",
                        "Low")
                }
                : Array.Empty<AlertCandidate>();

            await _reconciler.ReconcileAsync(projectId, candidates, AlertRuleCodes.ReviewRules, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
