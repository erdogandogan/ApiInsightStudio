using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class AnalysisResult
{
    /// <summary>Analiz sonucunun benzersiz kimliğidir.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Analizin ait olduğu projenin kimliğidir.</summary>
    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>API kalite skorunu 0 ile 100 arasında tutar.</summary>
    [Range(0, 100)]
    public int Score { get; set; }

    /// <summary>Analiz sonucunun oluşturulma tarihidir (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Analiz edilen proje kaydına erişim sağlar.</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Analiz sırasında üretilen uyarı listesini tutar.</summary>
    public ICollection<Warning> Warnings { get; set; } = new List<Warning>();
}
