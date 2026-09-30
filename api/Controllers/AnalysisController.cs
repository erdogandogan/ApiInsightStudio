using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalysisController : ControllerBase
{
    private readonly AnalysisService _analysisService;

    /// <summary>Analiz işlemlerini yürütmek için gerekli servis bağımlılığını alır.</summary>
    public AnalysisController(AnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    /// <summary>Belirtilen proje için kalite analizini çalıştırır ve sonucu döndürür.</summary>
    [HttpPost("{projectId}/analyze")]
    public async Task<IActionResult> AnalyzeProject([FromRoute] int projectId)
    {
        var analysisResult = await _analysisService.AnalyzeProjectAsync(projectId);

        return Ok(new
        {
            analysisResult.Id,
            analysisResult.ProjectId,
            analysisResult.Score,
            analysisResult.CreatedAt,
            warnings = analysisResult.Warnings.Select(warning => new
            {
                warning.Id,
                warning.EndpointId,
                warning.Message,
                warning.Severity
            })
        });
    }
}
