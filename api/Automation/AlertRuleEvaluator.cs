using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.Automation;

public static class AlertRuleCodes
{
    public const string LowScore = "LOW_SCORE";
    public const string MissingAuth = "MISSING_AUTH";
}

/// <summary>Bir analizin kural değerlendirmesi için gereken özet.</summary>
public sealed record AnalysisSnapshot(int Score, int MissingAuthCount);

/// <summary>Bir kuralın tuttuğunu söyleyen sonuç; henüz veritabanına yazılmış uyarı değildir.</summary>
public sealed record AlertCandidate(string RuleCode, string Message, string Severity);

/// <summary>
/// Veritabanına dokunmayan, tek başına test edilebilir kural motoru. Hangi kuralların tuttuğunu söyler;
/// uyarının açılması/çözülmesi olay işleyicisinin işidir.
/// </summary>
public class AlertRuleEvaluator
{
    public IReadOnlyList<AlertCandidate> Evaluate(AnalysisSnapshot analysis, ProjectAutomationSettings settings)
    {
        var candidates = new List<AlertCandidate>();
        if (!settings.Enabled)
            return candidates;

        if (analysis.Score < settings.ScoreThreshold)
        {
            candidates.Add(new AlertCandidate(
                AlertRuleCodes.LowScore,
                $"Kalite skoru {analysis.Score}, eşik değer ({settings.ScoreThreshold}) altında.",
                "Medium"));
        }

        if (settings.NotifyOnMissingAuth && analysis.MissingAuthCount > 0)
        {
            candidates.Add(new AlertCandidate(
                AlertRuleCodes.MissingAuth,
                $"{analysis.MissingAuthCount} kritik uç noktada kimlik doğrulama tanımı eksik.",
                "High"));
        }

        return candidates;
    }
}
