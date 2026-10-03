using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>Bir uyarı kuralının tutması sonucu açılan kayıt.</summary>
public class Alert
{
    public const string StatusOpen = "Open";
    public const string StatusResolved = "Resolved";

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>Kuralı tanımlayan kod (örn. LOW_SCORE).</summary>
    public string RuleCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    /// <summary>Open veya Resolved. Aynı proje ve kural için en fazla bir Open uyarı bulunur.</summary>
    public string Status { get; set; } = StatusOpen;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }

    public Project Project { get; set; } = null!;
}
