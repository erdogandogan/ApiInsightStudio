using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class Project
{
    /// <summary>Proje kimliği.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Proje adı.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Proje açıklaması.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>OpenAPI içeriği (JSON/YAML metni).</summary>
    public string OpenApiContent { get; set; } = string.Empty;

    /// <summary>Oluşturulma tarihi (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Kullanıcı kimliği (yabancı anahtar).</summary>
    [ForeignKey(nameof(User))]
    public int UserId { get; set; }

    /// <summary>Projeyi oluşturan kullanıcı.</summary>
    public User User { get; set; } = null!;

    /// <summary>Projeye ait endpoint'ler listesi.</summary>
    public ICollection<Endpoint> Endpoints { get; set; } = new List<Endpoint>();

    /// <summary>Projeye ait analiz sonuçları listesi.</summary>
    public ICollection<AnalysisResult> AnalysisResults { get; set; } = new List<AnalysisResult>();
}
