using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class Endpoint
{
    /// <summary>Endpoint kaydının benzersiz kimliğidir.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Endpoint'in ait olduğu projenin kimliğidir.</summary>
    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>Endpoint yol bilgisidir (örn: /api/users).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP metodunu belirtir (örn: GET, POST).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Endpoint için kısa açıklama/özet bilgisidir.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Endpoint'in gerektirdiği kimlik doğrulama türü (örn: Bearer, ApiKey). Eğer null ise herkese açıktır.</summary>
    public string? AuthType { get; set; }

    /// <summary>Endpoint'in bağlı olduğu proje bilgisidir.</summary>
    public Project Project { get; set; } = null!;

    /// <summary>Endpoint'e ait olası yanıtları listeler.</summary>
    public ICollection<Response> Responses { get; set; } = new List<Response>();

    /// <summary>Endpoint için üretilen test senaryolarını listeler.</summary>
    public ICollection<TestScenario> TestScenarios { get; set; } = new List<TestScenario>();
}
