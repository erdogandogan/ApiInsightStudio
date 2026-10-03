namespace ApiInsightStudio.Api.DTOs;

/// <summary>
/// Dashboard raporunda gösterilecek projeye ait özet bilgileri içerir.
/// </summary>
public class DashboardReportDto
{
    /// <summary>Projenin benzersiz kimliği.</summary>
    public int ProjectId { get; set; }

    /// <summary>Proje adı.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Projede tanımlı toplam endpoint sayısı.</summary>
    public int ToplamEndpointSayisi { get; set; }

    /// <summary>Kalite analizi sonucu ortaya çıkan skor (0-100).</summary>
    public int KaliteSkoru { get; set; }

    /// <summary>Analiz sırasında tespit edilen uyarılar listesi.</summary>
    public List<WarningDto> Warnings { get; set; } = new();

    /// <summary>Proje için üretilen test senaryoları listesi.</summary>
    public List<TestScenarioDto> TestScenarios { get; set; } = new();

    /// <summary>
    /// Uyarı özet bilgisini taşıyan iç sınıf.
    /// </summary>
    public class WarningDto
    {
        /// <summary>Uyarının metinsel açıklaması.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Uyarının önem seviyesi (High, Medium, Low).</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>Uyarının türü (Quality, Security).</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Uyarının ilişkili olduğu endpoint kimliği.</summary>
        public int? EndpointId { get; set; }

        /// <summary>Uyarının ait olduğu endpoint HTTP metodu.</summary>
        public string? EndpointMethod { get; set; }

        /// <summary>Uyarının ait olduğu endpoint yolu.</summary>
        public string? EndpointPath { get; set; }

        /// <summary>İlgili endpoint için kaydedilmiş AI açıklaması (varsa).</summary>
        public string? EndpointAiSummary { get; set; }
    }

    /// <summary>
    /// Test senaryosu özet bilgisini taşıyan iç sınıf.
    /// </summary>
    public class TestScenarioDto
    {
        /// <summary>Test senaryosunun başlığı.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>HTTP metodu (GET, POST, PUT, DELETE, vb.).</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>Endpoint yolu (örn: /api/users).</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Beklenen HTTP durum kodu.</summary>
        public int ExpectedStatusCode { get; set; }
    }
}
