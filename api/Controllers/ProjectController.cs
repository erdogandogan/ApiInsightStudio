using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Readers;
using EndpointModel = ApiInsightStudio.Api.Models.Endpoint;
using ResponseModel = ApiInsightStudio.Api.Models.Response;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly AnalysisService _analysisService;
    private readonly TestGenerationService _testGenerationService;
    private readonly AiService _aiService;

    public ProjectController(AppDbContext context, IConfiguration configuration,
        AnalysisService analysisService, TestGenerationService testGenerationService,
        AiService aiService)
    {
        _context = context;
        _configuration = configuration;
        _analysisService = analysisService;
        _testGenerationService = testGenerationService;
        _aiService = aiService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromBody] ProjectUploadDto dto)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
        {
            return Unauthorized(new { message = "Kullanıcı bulunamadı." });
        }

        if (!HasSupportedOpenApiVersion(dto.OpenApiContent, out var versionValidationError))
        {
            return BadRequest(new { message = versionValidationError });
        }

        var reader = new OpenApiStringReader();
        var defaultProjectName = _configuration["Jwt:Issuer"] ?? "Imported API";
        Microsoft.OpenApi.Models.OpenApiDocument? document = null;
        string[] diagnosticErrors = Array.Empty<string>();
        try
        {
            document = reader.Read(dto.OpenApiContent, out var diagnostic);
            diagnosticErrors = diagnostic.Errors.Select(e => e.Message).ToArray();
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = "OpenAPI içeriği parse edilemedi.",
                errors = new[] { ex.Message }
            });
        }

        if (diagnosticErrors.Length > 0)
        {
            return BadRequest(new
            {
                message = "OpenAPI içeriği parse edilemedi.",
                errors = diagnosticErrors
            });
        }

        if (document is null)
        {
            return BadRequest(new { message = "OpenAPI dokümanı okunamadı." });
        }

        var project = new Project
        {
            Name = string.IsNullOrWhiteSpace(dto.Name)
                ? (document.Info?.Title ?? defaultProjectName)
                : dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description)
                ? (document.Info?.Description ?? string.Empty)
                : dto.Description.Trim(),
            OpenApiContent = dto.OpenApiContent,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Projects.Add(project);

        var endpointCount = 0;
        var responseCount = 0;

        foreach (var pathEntry in document.Paths)
        {
            foreach (var operationEntry in pathEntry.Value.Operations)
            {
                // Operation seviyesinde security kontrolü yap
                string? authType = null;
                
                if (operationEntry.Value.Security != null && operationEntry.Value.Security.Count > 0)
                {
                    // Operation'da tanımlı security var, ilk olanı al
                    var operationSecurity = operationEntry.Value.Security.FirstOrDefault();
                    if (operationSecurity != null && operationSecurity.Keys.Count > 0)
                    {
                        authType = operationSecurity.Keys.First().Reference?.Id ?? "Bearer";
                    }
                }
                else if (document.SecurityRequirements != null && document.SecurityRequirements.Count > 0)
                {
                    // Global security tanımı varsa onu kullan
                    var globalSecurity = document.SecurityRequirements.FirstOrDefault();
                    if (globalSecurity != null && globalSecurity.Keys.Count > 0)
                    {
                        authType = globalSecurity.Keys.First().Reference?.Id ?? "Bearer";
                    }
                }

                var endpoint = new EndpointModel
                {
                    Project = project,
                    Path = pathEntry.Key,
                    Method = operationEntry.Key.ToString().ToUpperInvariant(),
                    Summary = operationEntry.Value.Summary ?? operationEntry.Value.Description ?? string.Empty,
                    AuthType = authType
                };

                foreach (var responseEntry in operationEntry.Value.Responses)
                {
                    endpoint.Responses.Add(new ResponseModel
                    {
                        StatusCode = responseEntry.Key,
                        Description = responseEntry.Value.Description ?? string.Empty
                    });

                    responseCount++;
                }

                _context.Endpoints.Add(endpoint);
                endpointCount++;
            }
        }

        await _context.SaveChangesAsync();

        await _analysisService.AnalyzeProjectAsync(project.Id);
        await _testGenerationService.GenerateTestsForProjectAsync(project.Id);

        return Ok(new
        {
            message = "OpenAPI dokümanı başarıyla yüklendi ve analiz edildi.",
            projectId = project.Id,
            endpointCount,
            responseCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects()
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });
        }

        var projects = await _context.Projects
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Name, p.CreatedAt })
            .ToListAsync();

        return Ok(projects);
    }

    /// <summary>
    /// Verilen proje ID'si için dashboard raporunu ve analiz sonuçlarını döndürür.
    /// </summary>
    [HttpGet("{projectId}/dashboard")]
    public async Task<IActionResult> GetDashboard([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        // Proje ve ilişkili verileri Eager Loading ile yükle; yalnızca kullanıcının kendi projesi
        var project = await _context.Projects
            .AsNoTracking()
            .Include(p => p.Endpoints)
                .ThenInclude(e => e.TestScenarios)
            .Include(p => p.AnalysisResults)
                .ThenInclude(ar => ar.Warnings)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);

        if (project == null)
        {
            return NotFound(new { message = "Proje bulunamadı." });
        }

        // En son analiz sonucunu al
        var latestAnalysis = project.AnalysisResults.OrderByDescending(ar => ar.CreatedAt).FirstOrDefault();

        // Dashboard DTO'yu oluştur
        var dashboard = new DashboardReportDto
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ToplamEndpointSayisi = project.Endpoints.Count,
            KaliteSkoru = latestAnalysis?.Score ?? 0,
            Warnings = latestAnalysis?.Warnings
                .Select(w =>
                {
                    var ep = w.EndpointId.HasValue
                        ? project.Endpoints.FirstOrDefault(e => e.Id == w.EndpointId.Value)
                        : null;
                    return new DashboardReportDto.WarningDto
                    {
                        Message = w.Message,
                        Severity = w.Severity,
                        Type = w.Type,
                        EndpointId = w.EndpointId,
                        EndpointMethod = ep?.Method,
                        EndpointPath = ep?.Path,
                        EndpointAiSummary = ep?.AiSummary
                    };
                })
                .ToList() ?? new(),
            TestScenarios = project.Endpoints
                .SelectMany(e => e.TestScenarios)
                .Select(ts => new DashboardReportDto.TestScenarioDto
                {
                    Title = ts.Title,
                    Method = ts.Method,
                    Path = ts.Path,
                    ExpectedStatusCode = ts.ExpectedStatusCode
                })
                .ToList()
        };

        return Ok(dashboard);
    }

    [HttpPost("{projectId}/endpoint/{endpointId}/generate-ai-description")]
    public async Task<IActionResult> GenerateAiDescription([FromRoute] int projectId, [FromRoute] int endpointId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        // Endpoint, kullanıcının kendi projesine ait değilse "bulunamadı" döner (varlığı sızdırılmaz)
        var endpoint = await _context.Endpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId
                                      && e.ProjectId == projectId
                                      && e.Project.UserId == userId);

        if (endpoint is null)
            return NotFound(new { message = "Endpoint bulunamadı." });

        string description;
        try
        {
            description = await _aiService.GenerateDescriptionAsync(endpoint.Method, endpoint.Path);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Yerel AI servisi (Ollama) çalışmıyor. Ollama'yı başlatıp 'qwen2.5:7b' modelinin yüklü olduğundan emin olun." });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { message = $"AI servisi şu an kullanılamıyor: {ex.Message}" });
        }

        // AI çıktısı yalnızca AiSummary'ye yazılır: Summary, skor ve uyarılar OpenAPI dokümanını yansıtmaya devam eder.
        endpoint.AiSummary = description;
        await _context.SaveChangesAsync();

        return Ok(new { message = "AI açıklaması başarıyla oluşturuldu.", description });
    }

    [HttpDelete("{projectId}")]
    public async Task<IActionResult> DeleteProject([FromRoute] int projectId)
    {
        var userIdClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);

        if (project is null)
            return NotFound(new { message = "Proje bulunamadı." });

        // FK kısıtlamaları nedeniyle manuel sıralı silme
        var endpointIds = await _context.Endpoints
            .Where(e => e.ProjectId == projectId)
            .Select(e => e.Id)
            .ToListAsync();

        var analysisResultIds = await _context.AnalysisResults
            .Where(ar => ar.ProjectId == projectId)
            .Select(ar => ar.Id)
            .ToListAsync();

        // Warnings (hem AnalysisResult hem Endpoint FK'ı var)
        await _context.Warnings
            .Where(w => analysisResultIds.Contains(w.AnalysisResultId))
            .ExecuteDeleteAsync();

        // TestScenarios, Responses
        await _context.TestScenarios
            .Where(ts => endpointIds.Contains(ts.EndpointId))
            .ExecuteDeleteAsync();

        await _context.Responses
            .Where(r => endpointIds.Contains(r.EndpointId))
            .ExecuteDeleteAsync();

        // AnalysisResults, Endpoints
        await _context.AnalysisResults
            .Where(ar => ar.ProjectId == projectId)
            .ExecuteDeleteAsync();

        await _context.Endpoints
            .Where(e => e.ProjectId == projectId)
            .ExecuteDeleteAsync();

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Proje başarıyla silindi." });
    }

    private static bool HasSupportedOpenApiVersion(string openApiContent, out string errorMessage)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(openApiContent);
            var root = jsonDocument.RootElement;

            if (root.TryGetProperty("openapi", out var openApiVersionElement))
            {
                var openApiVersion = openApiVersionElement.GetString();
                if (IsValidOpenApi3Version(openApiVersion))
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = "Geçersiz openapi sürümü. Desteklenen format: openapi: \"3.x.y\" (örn: 3.0.0).";
                return false;
            }

            if (root.TryGetProperty("swagger", out var swaggerVersionElement))
            {
                var swaggerVersion = swaggerVersionElement.GetString();
                if (swaggerVersion == "2.0")
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = "Geçersiz swagger sürümü. Desteklenen değer: swagger: \"2.0\".";
                return false;
            }

            errorMessage = "Sürüm alanı bulunamadı. Dokümana üst seviyede openapi: \"3.x.y\" veya swagger: \"2.0\" ekleyin.";
            return false;
        }
        catch (JsonException)
        {
            errorMessage = "OpenAPI içeriği geçerli JSON formatında değil.";
            return false;
        }
    }

    private static bool IsValidOpenApi3Version(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var parts = version.Split('.');
        return parts.Length == 3
               && parts[0] == "3"
               && int.TryParse(parts[1], out _)
               && int.TryParse(parts[2], out _);
    }
}
