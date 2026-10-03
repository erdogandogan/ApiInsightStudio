using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly AppDbContext _context;

    public AutomationController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Projenin otomasyon ayarlarını döndürür; kayıt yoksa varsayılanları gösterir.</summary>
    [HttpGet("{projectId}/settings")]
    public async Task<IActionResult> GetSettings([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        // Proje başkasına aitse "bulunamadı" döner (varlığı sızdırılmaz)
        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId);

        return Ok(ToDto(settings ?? new ProjectAutomationSettings { ProjectId = projectId }));
    }

    /// <summary>Projenin otomasyon ayarlarını oluşturur veya günceller.</summary>
    [HttpPut("{projectId}/settings")]
    public async Task<IActionResult> UpdateSettings([FromRoute] int projectId, [FromBody] AutomationSettingsDto dto)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (settings is null)
        {
            settings = new ProjectAutomationSettings { ProjectId = projectId };
            _context.ProjectAutomationSettings.Add(settings);
        }

        settings.ScoreThreshold = dto.ScoreThreshold;
        settings.NotifyOnMissingAuth = dto.NotifyOnMissingAuth;
        settings.Enabled = dto.Enabled;

        await _context.SaveChangesAsync();

        return Ok(ToDto(settings));
    }

    /// <summary>Projenin uyarılarını (en yeni önce) listeler. İsteğe bağlı olarak status=Open|Resolved ile süzülür.</summary>
    [HttpGet("{projectId}/alerts")]
    public async Task<IActionResult> GetAlerts([FromRoute] int projectId, [FromQuery] string? status = null)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        if (status is not null && status != Alert.StatusOpen && status != Alert.StatusResolved)
            return BadRequest(new { message = "status yalnızca Open veya Resolved olabilir." });

        var query = _context.Alerts.AsNoTracking().Where(a => a.ProjectId == projectId);
        if (status is not null)
            query = query.Where(a => a.Status == status);

        var alerts = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new AlertDto
            {
                Id = a.Id,
                RuleCode = a.RuleCode,
                Message = a.Message,
                Severity = a.Severity,
                Status = a.Status,
                CreatedAt = a.CreatedAt,
                ResolvedAt = a.ResolvedAt
            })
            .ToListAsync();

        return Ok(alerts);
    }

    private Task<bool> OwnsProjectAsync(int projectId, int userId) =>
        _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);

    private static AutomationSettingsDto ToDto(ProjectAutomationSettings settings) => new()
    {
        ScoreThreshold = settings.ScoreThreshold,
        NotifyOnMissingAuth = settings.NotifyOnMissingAuth,
        Enabled = settings.Enabled
    };
}
