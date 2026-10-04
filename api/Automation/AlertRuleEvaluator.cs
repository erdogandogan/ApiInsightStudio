using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.Automation;

public static class AlertRuleCodes
{
    public const string LowScore = "LOW_SCORE";
    public const string MissingAuth = "MISSING_AUTH";
    public const string TestFailures = "TEST_FAILURES";
    public const string PendingReview = "PENDING_REVIEW";

    /// <summary>Analiz tamamlanınca değerlendirilen kurallar.</summary>
    public static readonly IReadOnlyCollection<string> AnalysisRules = new[] { LowScore, MissingAuth };

    /// <summary>Test koşusu tamamlanınca değerlendirilen kurallar.</summary>
    public static readonly IReadOnlyCollection<string> TestRunRules = new[] { TestFailures };

    /// <summary>Onay bekleyen AI önerisi hatırlatması: olay değil, işçi turunda değerlendirilir.</summary>
    public static readonly IReadOnlyCollection<string> ReviewRules = new[] { PendingReview };
}

/// <summary>Bir analizin kural değerlendirmesi için gereken özet.</summary>
public sealed record AnalysisSnapshot(int Score, int MissingAuthCount);

/// <summary>Tamamlanmış bir test koşusunun kural değerlendirmesi için gereken özet.</summary>
public sealed record TestRunSnapshot(int Passed, int Failed, int Skipped)
{
    /// <summary>Gerçekten çalıştırılan test sayısı (atlananlar sayılmaz).</summary>
    public int Executed => Passed + Failed;

    /// <summary>Çalıştırılanlar içinde başarısız yüzdesi; hiç test çalışmadıysa 0.</summary>
    public double FailureRatePercent => Executed == 0 ? 0 : Failed * 100.0 / Executed;
}

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

    /// <summary>
    /// Test koşusu kuralı: çalıştırılan testlerde başarısızlık yüzdesi, projenin eşiğini AŞARSA (eşit olması yetmez) tutar.
    /// Hiç test çalışmadıysa (hepsi atlandı) çağıranın değerlendirme yapmaması gerekir; bu durumda kural tutmaz.
    /// </summary>
    public IReadOnlyList<AlertCandidate> EvaluateTestRun(TestRunSnapshot run, ProjectAutomationSettings settings)
    {
        var candidates = new List<AlertCandidate>();
        if (!settings.Enabled || run.Executed == 0)
            return candidates;

        if (run.FailureRatePercent > settings.TestFailureThresholdPercent)
        {
            candidates.Add(new AlertCandidate(
                AlertRuleCodes.TestFailures,
                $"Test koşusunda {run.Executed} testin {run.Failed} tanesi başarısız (%{run.FailureRatePercent:0}); eşik %{settings.TestFailureThresholdPercent}.",
                "High"));
        }

        return candidates;
    }
}
