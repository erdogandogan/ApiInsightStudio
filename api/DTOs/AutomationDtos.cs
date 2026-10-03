using System.ComponentModel.DataAnnotations;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.DTOs;

public class AutomationSettingsDto
{
    /// <summary>Kalite skoru bu değerin altına düşerse uyarı açılır (0-100).</summary>
    [Range(0, 100)]
    public int ScoreThreshold { get; set; } = ProjectAutomationSettings.DefaultScoreThreshold;

    public bool NotifyOnMissingAuth { get; set; } = true;

    public bool Enabled { get; set; } = true;
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
