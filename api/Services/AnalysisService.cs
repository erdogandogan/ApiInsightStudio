using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Services;

public class AnalysisService
{
    private readonly AppDbContext _dbContext;

    /// <summary>Veritabanı erişimi için gerekli DbContext bağımlılığını alır.</summary>
    public AnalysisService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Verilen projedeki endpoint'leri kurallara göre analiz ederek sonucu kaydeder.</summary>
    public async Task<AnalysisResult> AnalyzeProjectAsync(int projectId)
    {
        // İlgili projeye ait tüm endpoint'leri ve yanıtlarını tek seferde yükler.
        var endpoints = await _dbContext.Endpoints
            .AsNoTracking()
            .Include(endpoint => endpoint.Responses)
            .Where(endpoint => endpoint.ProjectId == projectId)
            .ToListAsync();

        var score = 100;
        var warnings = new List<Warning>();

        foreach (var endpoint in endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Summary))
            {
                score -= 10;
                warnings.Add(new Warning
                {
                    EndpointId = endpoint.Id,
                    Message = "Endpoint açıklaması eksik",
                    Severity = "Medium",
                    Type = "Quality"
                });
            }

            var hasErrorStatusCode = endpoint.Responses.Any(response =>
                response.StatusCode == "400" ||
                response.StatusCode == "401" ||
                response.StatusCode == "404");

            if (!hasErrorStatusCode)
            {
                score -= 15;
                warnings.Add(new Warning
                {
                    EndpointId = endpoint.Id,
                    Message = "Hata durum kodları tanımlanmamış",
                    Severity = "High",
                    Type = "Quality"
                });
            }

            // Güvenlik Kuralı 1: Kritik işlemler için Authentication tanımı yoksa
            var methodUpper = endpoint.Method?.ToUpperInvariant() ?? string.Empty;
            var isCriticalMethod = methodUpper == "POST" || methodUpper == "PUT" || methodUpper == "DELETE";
            if (isCriticalMethod && string.IsNullOrWhiteSpace(endpoint.AuthType))
            {
                score -= 20;
                warnings.Add(new Warning
                {
                    EndpointId = endpoint.Id,
                    Message = "Kritik uç noktada Authentication (Kimlik Doğrulama) tanımı eksik.",
                    Severity = "High",
                    Type = "Security"
                });
            }

            // Güvenlik Kuralı 2: DELETE + {id} parametresi
            if (methodUpper == "DELETE" &&
                !string.IsNullOrEmpty(endpoint.Path) &&
                endpoint.Path.Contains("{id}", StringComparison.OrdinalIgnoreCase))
            {
                score -= 15;
                warnings.Add(new Warning
                {
                    EndpointId = endpoint.Id,
                    Message = "ID bazlı veri silme işleminde yetkilendirme (BOLA) riski bulunuyor.",
                    Severity = "High",
                    Type = "Security"
                });
            }

            // Güvenlik Kuralı 3: Yanlış HTTP metodu (GET ile kalıcı işlem)
            if (methodUpper == "GET" &&
                !string.IsNullOrEmpty(endpoint.Path) &&
                (endpoint.Path.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                 endpoint.Path.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
                 endpoint.Path.Contains("update", StringComparison.OrdinalIgnoreCase)))
            {
                score -= 10;
                warnings.Add(new Warning
                {
                    EndpointId = endpoint.Id,
                    Message = "GET metodu ile veri üzerinde kalıcı işlem yapılması REST güvenliğine aykırıdır.",
                    Severity = "Medium",
                    Type = "Security"
                });
            }
        }

        // Proje başına tek analiz sonucu tutulur: varsa en yenisi yerinde güncellenir (upsert),
        // eski kopyalar silinir. Geçmişi okuyan bir yer yok; dashboard yalnızca en yenisini kullanır.
        var existing = await _dbContext.AnalysisResults
            .Include(analysis => analysis.Warnings)
            .Where(analysis => analysis.ProjectId == projectId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .ThenByDescending(analysis => analysis.Id)
            .ToListAsync();

        var analysisResult = existing.FirstOrDefault();
        if (analysisResult is null)
        {
            analysisResult = new AnalysisResult { ProjectId = projectId };
            _dbContext.AnalysisResults.Add(analysisResult);
        }
        else
        {
            _dbContext.Warnings.RemoveRange(analysisResult.Warnings);
            analysisResult.Warnings.Clear();

            foreach (var duplicate in existing.Skip(1))
                _dbContext.Warnings.RemoveRange(duplicate.Warnings);
            _dbContext.AnalysisResults.RemoveRange(existing.Skip(1));
        }

        analysisResult.Score = Math.Max(0, score);
        analysisResult.CreatedAt = DateTime.UtcNow;
        foreach (var warning in warnings)
            analysisResult.Warnings.Add(warning);

        await _dbContext.SaveChangesAsync();

        return analysisResult;
    }
}
