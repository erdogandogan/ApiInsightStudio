using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>Bir projenin otomasyon (uyarı) ayarları. Satır yoksa varsayılanlar geçerlidir.</summary>
public class ProjectAutomationSettings
{
    public const int DefaultScoreThreshold = 60;
    public const int DefaultTestFailureThresholdPercent = 20;

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>Kalite skoru bu değerin altına düşerse uyarı açılır.</summary>
    public int ScoreThreshold { get; set; } = DefaultScoreThreshold;

    /// <summary>Kritik uç noktada kimlik doğrulama eksikse uyarı açılır.</summary>
    public bool NotifyOnMissingAuth { get; set; } = true;

    /// <summary>Kapalıysa bu proje için hiçbir uyarı kuralı çalışmaz.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Otomatik test koşusunun istek atacağı hedef API'nin kök adresi (sorgu/parça içermez). Boşsa test koşusu başlatılamaz.
    /// </summary>
    public string? TargetBaseUrl { get; set; }

    /// <summary>Hedef API için isteğe bağlı Bearer token, Data Protection ile şifreli. API'den okunamaz.</summary>
    public string? TargetBearerTokenProtected { get; set; }

    /// <summary>
    /// Kapalıyken (varsayılan) yalnızca GET çalışır. Açılırsa mutating metotlarda (POST/PUT/PATCH/DELETE)
    /// yalnızca olumsuz senaryolar (401/400/404) çalışır; başarılı senaryolar hiçbir zaman çalıştırılmaz.
    /// </summary>
    public bool AllowMutatingTests { get; set; }

    /// <summary>Çalıştırılan testlerde başarısızlık yüzdesi bunu aşarsa TEST_FAILURES uyarısı açılır (0-100).</summary>
    public int TestFailureThresholdPercent { get; set; } = DefaultTestFailureThresholdPercent;

    /// <summary>Uyarılar sunucuda yapılandırılmış Telegram sohbetine de gönderilsin mi?</summary>
    public bool NotifyTelegram { get; set; }

    /// <summary>Uyarıların imzalı POST ile gönderileceği adres. Boşsa webhook kanalı kapalıdır.</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Webhook imza sırrı, Data Protection ile şifreli. Düz hali veritabanında tutulmaz ve API'den okunamaz.</summary>
    public string? WebhookSecretProtected { get; set; }

    public Project Project { get; set; } = null!;
}
