using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>Bir projenin otomasyon (uyarı) ayarları. Satır yoksa varsayılanlar geçerlidir.</summary>
public class ProjectAutomationSettings
{
    public const int DefaultScoreThreshold = 60;

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

    public Project Project { get; set; } = null!;
}
