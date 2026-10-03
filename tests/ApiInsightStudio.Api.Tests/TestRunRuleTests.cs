using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.AspNetCore.DataProtection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Test koşusu başarısızlık kuralı (saf kural motoru) ve hedef token'ının şifreleme amacı.</summary>
public class TestRunRuleTests
{
    private readonly AlertRuleEvaluator _evaluator = new();

    private static ProjectAutomationSettings Settings(int threshold = 20, bool enabled = true) =>
        new() { TestFailureThresholdPercent = threshold, Enabled = enabled };

    [Fact]
    public void Varsayilan_esik_yuzde_20dir()
    {
        Assert.Equal(20, new ProjectAutomationSettings().TestFailureThresholdPercent);
        Assert.False(new ProjectAutomationSettings().AllowMutatingTests);
    }

    [Fact]
    public void Esigi_asan_basarisizlik_orani_TEST_FAILURES_tetikler()
    {
        var result = _evaluator.EvaluateTestRun(new TestRunSnapshot(Passed: 7, Failed: 3, Skipped: 0), Settings(20));

        var candidate = Assert.Single(result);
        Assert.Equal(AlertRuleCodes.TestFailures, candidate.RuleCode);
        Assert.Equal("High", candidate.Severity);
        Assert.Contains("3", candidate.Message);
        Assert.Contains("10", candidate.Message);
        Assert.Contains("%30", candidate.Message);
        Assert.Contains("%20", candidate.Message);
    }

    [Fact]
    public void Oran_esige_esitse_tetiklenmez_yalnizca_asarsa_tetiklenir()
    {
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(8, 2, 0), Settings(20)));   // tam %20
        Assert.Single(_evaluator.EvaluateTestRun(new TestRunSnapshot(79, 21, 0), Settings(20))); // %21
    }

    [Fact]
    public void Atlananlar_orana_katilmaz()
    {
        // 1 başarısız / 5 çalışan = %20 (eşiği aşmaz); 100 atlanan oranı seyreltmez ya da şişirmez
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(4, 1, 100), Settings(20)));
        Assert.Single(_evaluator.EvaluateTestRun(new TestRunSnapshot(1, 1, 100), Settings(20)));
    }

    [Fact]
    public void Hic_test_calismadiysa_kural_tetiklenmez()
    {
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(0, 0, 11), Settings(0)));
    }

    [Fact]
    public void Esik_sifirsa_tek_basarisizlik_bile_tetikler()
    {
        Assert.Single(_evaluator.EvaluateTestRun(new TestRunSnapshot(99, 1, 0), Settings(0)));
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(100, 0, 0), Settings(0)));
    }

    [Fact]
    public void Esik_yuzse_hicbir_oran_tetiklemez()
    {
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(0, 50, 0), Settings(100)));
    }

    [Fact]
    public void Proje_kapaliysa_kural_calismaz()
    {
        Assert.Empty(_evaluator.EvaluateTestRun(new TestRunSnapshot(0, 10, 0), Settings(20, enabled: false)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(5, 5, 0, 50)]
    [InlineData(1, 3, 9, 75)]
    [InlineData(0, 4, 0, 100)]
    [InlineData(4, 0, 2, 0)]
    public void Anlik_goruntu_yuzdesi(int passed, int failed, int skipped, double expectedRate)
    {
        var snapshot = new TestRunSnapshot(passed, failed, skipped);

        Assert.Equal(passed + failed, snapshot.Executed);
        Assert.Equal(expectedRate, snapshot.FailureRatePercent, precision: 6);
    }

    [Fact]
    public void Kural_kodlari_ayri_kume_olarak_yonetilir()
    {
        Assert.Equal(new[] { AlertRuleCodes.LowScore, AlertRuleCodes.MissingAuth }.OrderBy(x => x),
            AlertRuleCodes.AnalysisRules.OrderBy(x => x));
        Assert.Equal(new[] { AlertRuleCodes.TestFailures }, AlertRuleCodes.TestRunRules);
        Assert.Empty(AlertRuleCodes.AnalysisRules.Intersect(AlertRuleCodes.TestRunRules));
    }

    // ----- Hedef token'ı şifreleme amacı -----

    [Fact]
    public void Hedef_tokeni_sifrelenip_cozulebilir_ve_sifreli_hali_tokeni_icermez()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());

        var protectedValue = protector.ProtectToken("hedef-token-123");

        Assert.DoesNotContain("hedef-token-123", protectedValue);
        Assert.Equal("hedef-token-123", protector.UnprotectToken(protectedValue));
    }

    [Fact]
    public void Webhook_sirri_ve_hedef_tokeni_birbirinin_yerine_cozulemez()
    {
        var protector = new WebhookSecretProtector(new EphemeralDataProtectionProvider());

        var tokenCipher = protector.ProtectToken("hedef-token");
        var secretCipher = protector.Protect("whsec_x");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(tokenCipher));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.UnprotectToken(secretCipher));
    }
}
