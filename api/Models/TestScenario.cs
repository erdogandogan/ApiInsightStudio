using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

public class TestScenario
{
    /// <summary>Test senaryosu kaydının benzersiz kimliğidir.</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Test senaryosunun ait olduğu endpoint kimliğidir.</summary>
    [ForeignKey(nameof(Endpoint))]
    public int EndpointId { get; set; }

    /// <summary>Test senaryosunun başlığını tutar (örn: Başarılı İstek).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Testte kullanılacak HTTP metodunu belirtir (örn: GET).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Testte hedeflenecek endpoint yolunu tutar (örn: /api/users).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Beklenen HTTP durum kodunu belirtir (örn: 200, 400).</summary>
    public int ExpectedStatusCode { get; set; }

    /// <summary>Test senaryosunun neyi doğruladığını açıklayan metindir.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Test senaryosunun bağlı olduğu endpoint bilgisidir.</summary>
    public Endpoint Endpoint { get; set; } = null!;
}
