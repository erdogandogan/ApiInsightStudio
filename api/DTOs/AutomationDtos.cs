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
}

public class AutomationSettingsResponseDto : AutomationSettingsDto
{
    /// <summary>Webhook için bir imza sırrı üretilmiş mi? (Sırrın kendisi bir daha okunamaz.)</summary>
    public bool WebhookSecretConfigured { get; set; }

    /// <summary>Sunucuda Telegram (bot token ve sohbet kimliği) yapılandırılmış mı?</summary>
    public bool TelegramAvailable { get; set; }

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
