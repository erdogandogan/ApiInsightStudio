using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Services;

public class TestGenerationService
{
    private readonly AppDbContext _dbContext;

    /// <summary>
    /// Veritabanı erişimi için gerekli DbContext bağımlılığını alır.
    /// </summary>
    public TestGenerationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Verilen proje için otomatik test senaryoları üretir. Önceden üretilmiş testler varsa onları siler.
    /// </summary>
    public async Task GenerateTestsForProjectAsync(int projectId)
    {
        // Projeye ait endpointleri çek
        var endpoints = await _dbContext.Endpoints
            .Where(e => e.ProjectId == projectId)
            .ToListAsync();

        var endpointIds = endpoints.Select(e => e.Id).ToList();

        // Var olan test senaryolarını temizle (duplicate önlemek için)
        var existing = await _dbContext.TestScenarios
            .Where(t => endpointIds.Contains(t.EndpointId))
            .ToListAsync();

        if (existing.Any())
        {
            _dbContext.TestScenarios.RemoveRange(existing);
            await _dbContext.SaveChangesAsync();
        }

        var newScenarios = new List<TestScenario>();

        foreach (var endpoint in endpoints)
        {
            // Her zaman: Başarılı istek (200)
            newScenarios.Add(new TestScenario
            {
                EndpointId = endpoint.Id,
                Title = "Başarılı İstek",
                Method = endpoint.Method,
                Path = endpoint.Path,
                ExpectedStatusCode = 200,
                Description = "Başarılı normal istek için beklenen durum kodu."
            });

            // Her zaman: Yetkisiz erişim (401)
            newScenarios.Add(new TestScenario
            {
                EndpointId = endpoint.Id,
                Title = "Yetkisiz Erişim (Token Yok)",
                Method = endpoint.Method,
                Path = endpoint.Path,
                ExpectedStatusCode = 401,
                Description = "Yetkilendirme olmadan erişimde beklenen hata kodu."
            });

            var methodUpper = endpoint.Method?.ToUpperInvariant() ?? string.Empty;
            if (methodUpper == "POST" || methodUpper == "PUT")
            {
                // POST veya PUT için 400 senaryosu
                newScenarios.Add(new TestScenario
                {
                    EndpointId = endpoint.Id,
                    Title = "Eksik/Hatalı Payload",
                    Method = endpoint.Method,
                    Path = endpoint.Path,
                    ExpectedStatusCode = 400,
                    Description = "Eksik veya hatalı payload gönderildiğinde beklenen durum kodu."
                });
            }

            if (!string.IsNullOrEmpty(endpoint.Path) && endpoint.Path.Contains('{'))
            {
                // Path içinde { varsa 404 senaryosu
                newScenarios.Add(new TestScenario
                {
                    EndpointId = endpoint.Id,
                    Title = "Kaynak Bulunamadı (Geçersiz ID)",
                    Method = endpoint.Method,
                    Path = endpoint.Path,
                    ExpectedStatusCode = 404,
                    Description = "Geçersiz ID ile erişim durumunda beklenen durum kodu."
                });
            }
        }

        if (newScenarios.Any())
        {
            await _dbContext.TestScenarios.AddRangeAsync(newScenarios);
            await _dbContext.SaveChangesAsync();
        }
    }
}
