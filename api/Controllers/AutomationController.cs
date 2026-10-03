using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AutomationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly WebhookSecretProtector _secrets;
    private readonly NotificationOptions _notificationOptions;

    public AutomationController(
        AppDbContext context, WebhookSecretProtector secrets, IOptions<NotificationOptions> notificationOptions)
    {
        _context = context;
        _secrets = secrets;
        _notificationOptions = notificationOptions.Value;
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

    /// <summary>
    /// Projenin otomasyon ayarlarını oluşturur veya tamamen değiştirir. Webhook adresi ilk kez verilirse
    /// bir imza sırrı üretilir ve cevapta yalnızca bir kez gösterilir.
    /// </summary>
    [HttpPut("{projectId}/settings")]
    public async Task<IActionResult> UpdateSettings([FromRoute] int projectId, [FromBody] AutomationSettingsDto dto)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var webhookUrl = string.IsNullOrWhiteSpace(dto.WebhookUrl) ? null : dto.WebhookUrl.Trim();
        if (webhookUrl is not null)
        {
            var check = await UrlSafety.ValidateAsync(webhookUrl, _notificationOptions.AllowedPrivateHosts, HttpContext.RequestAborted);
            if (!check.Ok)
                return BadRequest(new { message = $"Webhook adresi reddedildi: {check.Error}" });
        }

        var settings = await _context.ProjectAutomationSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (settings is null)
        {
            settings = new ProjectAutomationSettings { ProjectId = projectId };
            _context.ProjectAutomationSettings.Add(settings);
        }

        settings.ScoreThreshold = dto.ScoreThreshold;
        settings.NotifyOnMissingAuth = dto.NotifyOnMissingAuth;
        settings.Enabled = dto.Enabled;
        settings.WebhookUrl = webhookUrl;

        string? newSecret = null;
        if (webhookUrl is null)
        {
            // Webhook kapatılınca sır da silinir
            settings.WebhookSecretProtected = null;
        }
        else if (string.IsNullOrEmpty(settings.WebhookSecretProtected))
        {
            newSecret = _secrets.Generate();
            settings.WebhookSecretProtected = _secrets.Protect(newSecret);
        }

        await _context.SaveChangesAsync();

        var response = ToDto(settings);
        response.NewWebhookSecret = newSecret;
        return Ok(response);
    }

    /// <summary>Webhook imza sırrını yeniler. Yeni sır cevapta yalnızca bir kez gösterilir; eskisi geçersiz olur.</summary>
    [HttpPost("{projectId}/webhook/secret")]
    public async Task<IActionResult> RotateWebhookSecret([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (settings is null || string.IsNullOrWhiteSpace(settings.WebhookUrl))
            return BadRequest(new { message = "Önce bir webhook adresi ayarlayın." });

        var secret = _secrets.Generate();
        settings.WebhookSecretProtected = _secrets.Protect(secret);
        await _context.SaveChangesAsync();

        return Ok(new { webhookSecret = secret });
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

    /// <summary>Projenin bildirim teslimatlarını (en yeni önce) listeler. İsteğe bağlı olarak status=Pending|Succeeded|Dead ile süzülür.</summary>
    [HttpGet("{projectId}/deliveries")]
    public async Task<IActionResult> GetDeliveries([FromRoute] int projectId, [FromQuery] string? status = null)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        if (status is not null
            && status != NotificationDelivery.StatusPending
            && status != NotificationDelivery.StatusSucceeded
            && status != NotificationDelivery.StatusDead)
            return BadRequest(new { message = "status yalnızca Pending, Succeeded veya Dead olabilir." });

        var query = _context.NotificationDeliveries.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (status is not null)
            query = query.Where(d => d.Status == status);

        var deliveries = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new DeliveryDto
            {
                Id = d.Id,
                EventId = d.EventId,
                Channel = d.Channel,
                Status = d.Status,
                Attempts = d.Attempts,
                NextAttemptAt = d.NextAttemptAt,
                LastStatusCode = d.LastStatusCode,
                LastError = d.LastError,
                CreatedAt = d.CreatedAt,
                CompletedAt = d.CompletedAt
            })
            .ToListAsync();

        return Ok(deliveries);
    }

    private Task<bool> OwnsProjectAsync(int projectId, int userId) =>
        _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);

    private static AutomationSettingsResponseDto ToDto(ProjectAutomationSettings settings) => new()
    {
        ScoreThreshold = settings.ScoreThreshold,
        NotifyOnMissingAuth = settings.NotifyOnMissingAuth,
        Enabled = settings.Enabled,
        WebhookUrl = settings.WebhookUrl,
        WebhookSecretConfigured = !string.IsNullOrEmpty(settings.WebhookSecretProtected)
    };
}
