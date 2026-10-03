using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Kural motoru veritabanına dokunmaz; hangi kuralın ne zaman tuttuğunu saf olarak doğrular.</summary>
public class AlertRuleEvaluatorTests
{
    private readonly AlertRuleEvaluator _evaluator = new();

    private static ProjectAutomationSettings Settings(
        int threshold = 60, bool notifyOnMissingAuth = true, bool enabled = true) =>
        new() { ScoreThreshold = threshold, NotifyOnMissingAuth = notifyOnMissingAuth, Enabled = enabled };

    [Fact]
    public void Varsayilan_ayarlar_esik_60_ve_kimlik_uyarisi_aciktir()
    {
        var settings = new ProjectAutomationSettings();

        Assert.Equal(60, settings.ScoreThreshold);
        Assert.True(settings.NotifyOnMissingAuth);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void Skor_esigin_altindaysa_LOW_SCORE_tutar()
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(59, 0), Settings(threshold: 60));

        var candidate = Assert.Single(result);
        Assert.Equal(AlertRuleCodes.LowScore, candidate.RuleCode);
        Assert.Contains("59", candidate.Message);
        Assert.Contains("60", candidate.Message);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(100)]
    public void Skor_esige_esit_veya_ustundeyse_LOW_SCORE_tutmaz(int score)
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(score, 0), Settings(threshold: 60));

        Assert.Empty(result);
    }

    [Fact]
    public void Esik_ayardan_gelir()
    {
        Assert.Empty(_evaluator.Evaluate(new AnalysisSnapshot(40, 0), Settings(threshold: 40)));
        Assert.Single(_evaluator.Evaluate(new AnalysisSnapshot(40, 0), Settings(threshold: 41)));
    }

    [Fact]
    public void Esik_sifirsa_hicbir_skor_LOW_SCORE_tetiklemez()
    {
        Assert.Empty(_evaluator.Evaluate(new AnalysisSnapshot(0, 0), Settings(threshold: 0)));
    }

    [Fact]
    public void Kimlik_dogrulamasi_eksik_uc_nokta_varsa_MISSING_AUTH_tutar()
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(100, 3), Settings());

        var candidate = Assert.Single(result);
        Assert.Equal(AlertRuleCodes.MissingAuth, candidate.RuleCode);
        Assert.Equal("High", candidate.Severity);
        Assert.Contains("3", candidate.Message);
    }

    [Fact]
    public void Ayar_kapaliysa_MISSING_AUTH_tutmaz()
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(100, 3), Settings(notifyOnMissingAuth: false));

        Assert.Empty(result);
    }

    [Fact]
    public void Iki_kural_ayni_anda_tutabilir()
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(30, 2), Settings());

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.RuleCode == AlertRuleCodes.LowScore);
        Assert.Contains(result, c => c.RuleCode == AlertRuleCodes.MissingAuth);
    }

    [Fact]
    public void Proje_kapaliysa_hicbir_kural_calismaz()
    {
        var result = _evaluator.Evaluate(new AnalysisSnapshot(0, 5), Settings(enabled: false));

        Assert.Empty(result);
    }
}
