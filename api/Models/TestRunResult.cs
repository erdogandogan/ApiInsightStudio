using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>
/// Bir koşudaki tek bir senaryonun sonucu. Senaryo alanları kopyalanır (anlık görüntü): senaryolar yeniden
/// üretildiğinde eski koşuların sonuçları anlamını kaybetmesin.
/// </summary>
public class TestRunResult
{
    public const string OutcomePassed = "Passed";
    public const string OutcomeFailed = "Failed";
    public const string OutcomeSkipped = "Skipped";

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(TestRun))]
    public int TestRunId { get; set; }

    public int? EndpointId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    /// <summary>OpenAPI'deki yol şablonu (örn. /items/{id}).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gerçekte istek atılan yol (parametreler doldurulmuş, ana bilgisayar yok). Atlandıysa null.</summary>
    public string? RequestPath { get; set; }

    public int ExpectedStatusCode { get; set; }

    public int? ActualStatusCode { get; set; }

    public string Outcome { get; set; } = OutcomeSkipped;

    public int DurationMs { get; set; }

    /// <summary>Atlama/başarısızlık nedeni (hassas veri içermez).</summary>
    public string? Reason { get; set; }

    public TestRun TestRun { get; set; } = null!;
}
