using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class Warning
{
    /// <summary>Uyarı kaydının benzersiz kimliğidir.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Uyarının bağlı olduğu analiz sonucunun kimliğidir.</summary>
    [ForeignKey(nameof(AnalysisResult))]
    public int AnalysisResultId { get; set; }

    /// <summary>Uyarı belirli bir endpoint'e aitse endpoint kimliğini tutar.</summary>
    [ForeignKey(nameof(Endpoint))]
    public int? EndpointId { get; set; }

    /// <summary>Uyarının metinsel açıklamasını içerir.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Uyarı seviyesini belirtir (High, Medium, Low).</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>Uyarının türünü belirtir (Quality, Security).</summary>
    public string Type { get; set; } = "Quality";

    /// <summary>Uyarının bağlı olduğu analiz sonucu bilgisidir.</summary>
    public AnalysisResult AnalysisResult { get; set; } = null!;

    /// <summary>Uyarı endpoint bazlıysa ilgili endpoint bilgisine erişim sağlar.</summary>
    public Endpoint? Endpoint { get; set; }
}
