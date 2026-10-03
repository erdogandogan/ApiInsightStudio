using System.ComponentModel.DataAnnotations;

namespace ApiInsightStudio.Api.Models;

/// <summary>
/// "Yapılacak işler defteri" satırı: bir olay, onu üreten kayıt işlemiyle aynı işlemde buraya yazılır
/// ve sonra işlenir. Sistem arada çökse bile olay kaybolmaz.
/// </summary>
public class OutboxMessage
{
    [Key]
    public int Id { get; set; }

    /// <summary>Olay türünün adı (örn. AnalysisCompleted).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Olayın JSON içeriği.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Başarıyla işlendiği zaman; null ise henüz işlenmedi.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Başarısız işleme denemesi sayısı.</summary>
    public int Attempts { get; set; }

    /// <summary>Son hatanın kısa metni (hassas veri içermez).</summary>
    public string? LastError { get; set; }
}
