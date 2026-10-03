using System.ComponentModel.DataAnnotations;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.DTOs;

/// <summary>Ayarları günceller. PUT tam değiştirmedir: webhookUrl boş/null gönderilirse webhook kapatılır.</summary>
public class AutomationSettingsDto
{
    /// <summary>Kalite skoru bu değerin altına düşerse uyarı açılır (0-100).</summary>
    [Range(0, 100)]
    public int ScoreThreshold { get; set; } = ProjectAutomationSettings.DefaultScoreThreshold;

    public bool NotifyOnMissingAuth { get; set; } = true;

    public bool Enabled { get; set; } = true;

    /// <summary>Uyarılar Telegram'a da gönderilsin mi? Yalnızca sunucuda Telegram yapılandırılmışsa açılabilir.</summary>
    public bool NotifyTelegram { get; set; }

    /// <summary>Uyarıların imzalı POST ile gönderileceği https adresi. Boş/null ise webhook kapalıdır.</summary>
    [StringLength(2048)]
    public string? WebhookUrl { get; set; }

    /// <summary>
    /// Otomatik test koşusunun istek atacağı hedef API'nin kök https adresi (sorgu/parça içermez). Boş/null ise test koşusu
    /// başlatılamaz. Adres (şema/ana bilgisayar/port) değişirse veya silinirse hedef token'ı da silinir.
    /// </summary>
    [StringLength(2048)]
    public string? TargetBaseUrl { get; set; }

    /// <summary>
    /// Varsayılan kapalı: yalnızca GET çalışır. Açılırsa mutating metotlarda (POST/PUT/PATCH/DELETE) yalnızca olumsuz
    /// senaryolar (401/400/404) çalışır; başarılı senaryolar hiçbir zaman çalıştırılmaz.
    /// </summary>
    public bool AllowMutatingTests { get; set; }

    /// <summary>Çalıştırılan testlerde başarısızlık yüzdesi bunu aşarsa TEST_FAILURES uyarısı açılır (0-100).</summary>
    [Range(0, 100)]
    public int TestFailureThresholdPercent { get; set; } = ProjectAutomationSettings.DefaultTestFailureThresholdPercent;
}

/// <summary>Hedef API için Bearer token'ı. Yalnızca yazılır; hiçbir cevapta geri döndürülmez.</summary>
public class TargetTokenDto
{
    [Required]
    [StringLength(4096, MinimumLength = 1)]
    public string Token { get; set; } = string.Empty;
}

public class AutomationSettingsResponseDto : AutomationSettingsDto
{
    /// <summary>Webhook için bir imza sırrı üretilmiş mi? (Sırrın kendisi bir daha okunamaz.)</summary>
    public bool WebhookSecretConfigured { get; set; }

    /// <summary>Sunucuda Telegram (bot token ve sohbet kimliği) yapılandırılmış mı?</summary>
    public bool TelegramAvailable { get; set; }

    /// <summary>Hedef API için Bearer token tanımlı mı? (Token'ın kendisi bir daha okunamaz.)</summary>
    public bool TargetTokenConfigured { get; set; }

    /// <summary>Yalnızca sır az önce üretildiyse dolu gelir ve bir daha gösterilmez. Alıcı tarafta imzayı doğrulamak için saklayın.</summary>
    public string? NewWebhookSecret { get; set; }
}

public class AlertDto
{
    public int Id { get; set; }

    public string RuleCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}

public class TestRunDto
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int Total { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public string? TargetHost { get; set; }

    public string? Error { get; set; }
}

public class TestRunResultDto
{
    public string Title { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? RequestPath { get; set; }

    public int ExpectedStatusCode { get; set; }

    public int? ActualStatusCode { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    public string? Reason { get; set; }
}

public class TestRunDetailDto : TestRunDto
{
    public List<TestRunResultDto> Results { get; set; } = new();
}

public class DeliveryDto
{
    public int Id { get; set; }

    public Guid EventId { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
