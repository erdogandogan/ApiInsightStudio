using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalysisController : ControllerBase
{
    private readonly AnalysisService _analysisService;
    private readonly AppDbContext _context;

    /// <summary>Analiz işlemlerini yürütmek için gerekli servis ve veritabanı bağımlılıklarını alır.</summary>
    public AnalysisController(AnalysisService analysisService, AppDbContext context)
    {
        _analysisService = analysisService;
        _context = context;
    }

    /// <summary>Belirtilen proje için kalite analizini çalıştırır ve sonucu döndürür.</summary>
    [HttpPost("{projectId}/analyze")]
    public async Task<IActionResult> AnalyzeProject([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        // Proje başkasına aitse "bulunamadı" döner (varlığı sızdırılmaz)
        var ownsProject = await _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);
        if (!ownsProject)
            return NotFound(new { message = "Proje bulunamadı." });

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
