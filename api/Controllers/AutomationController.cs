using ApiInsightStudio.Api.Audit;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using ApiInsightStudio.Api.TestRunner;
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
    private readonly TelegramOptions _telegramOptions;
    private readonly IEventPublisher _events;
    private readonly OutboxDispatcher _dispatcher;
    private readonly DeliveryProcessor _deliveries;
    private readonly AuditTrail _audit;

    public AutomationController(
        AppDbContext context,
        WebhookSecretProtector secrets,
        IOptions<NotificationOptions> notificationOptions,
        IOptions<TelegramOptions> telegramOptions,
        IEventPublisher events,
        OutboxDispatcher dispatcher,
        DeliveryProcessor deliveries,
        AuditTrail audit)
    {
        _audit = audit;
        _context = context;
        _secrets = secrets;
        _notificationOptions = notificationOptions.Value;
        _telegramOptions = telegramOptions.Value;
        _events = events;
        _dispatcher = dispatcher;
        _deliveries = deliveries;
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

        if (dto.NotifyTelegram && !_telegramOptions.IsConfigured)
            return BadRequest(new { message = "Telegram bu sunucuda yapılandırılmamış (Telegram:BotToken ve Telegram:ChatId user-secrets ile verilmeli)." });

        var webhookUrl = string.IsNullOrWhiteSpace(dto.WebhookUrl) ? null : dto.WebhookUrl.Trim();
        if (webhookUrl is not null)
        {
            var check = await UrlSafety.ValidateAsync(webhookUrl, _notificationOptions.AllowedPrivateHosts, HttpContext.RequestAborted);
            if (!check.Ok)
                return BadRequest(new { message = $"Webhook adresi reddedildi: {check.Error}" });
        }

        var targetUrl = string.IsNullOrWhiteSpace(dto.TargetBaseUrl) ? null : dto.TargetBaseUrl.Trim();
        if (targetUrl is not null)
        {
            var check = await TargetUrl.ValidateAsync(targetUrl, _notificationOptions.AllowedPrivateHosts, HttpContext.RequestAborted);
            if (!check.Ok)
                return BadRequest(new { message = $"Hedef adres reddedildi: {check.Error}" });
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
        settings.NotifyTelegram = dto.NotifyTelegram;
        settings.WebhookUrl = webhookUrl;
        settings.AllowMutatingTests = dto.AllowMutatingTests;
        settings.TestFailureThresholdPercent = dto.TestFailureThresholdPercent;

        // Hedef token'ı yalnızca verildiği adrese gitmeli: adres silinirse ya da şema/ana bilgisayar/port değişirse
        // token da silinir (yeni bir adrese yanlışlıkla gönderilmesin).
        if (targetUrl is null || !SameOrigin(settings.TargetBaseUrl, targetUrl))
            settings.TargetBearerTokenProtected = null;
        settings.TargetBaseUrl = targetUrl;

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

        // Denetim izine yalnızca gizli olmayan özet yazılır: adresler için yalnızca ana bilgisayar, sır/token hiç yok.
        await _audit.AppendAsync(projectId, userId, AuditActions.SettingsUpdated, null,
            $"enabled={Flag(settings.Enabled)}; scoreThreshold={settings.ScoreThreshold}; missingAuth={Flag(settings.NotifyOnMissingAuth)}; webhook={HostOf(settings.WebhookUrl)}; telegram={Flag(settings.NotifyTelegram)}; target={HostOf(settings.TargetBaseUrl)}; mutatingTests={Flag(settings.AllowMutatingTests)}; testFailureThreshold={settings.TestFailureThresholdPercent}");
        await _context.SaveChangesAsync();

        var response = ToDto(settings);
        response.NewWebhookSecret = newSecret;
        return Ok(response);
    }

    /// <summary>
    /// Hedef API için Bearer token'ı tanımlar (önce targetBaseUrl ayarlanmalı). Token şifreli saklanır ve bir daha
    /// okunamaz; yalnızca hedef adrese, 401 senaryosu dışındaki isteklerde gönderilir.
    /// </summary>
    [HttpPut("{projectId}/target-token")]
    public async Task<IActionResult> SetTargetToken([FromRoute] int projectId, [FromBody] TargetTokenDto dto)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (settings is null || string.IsNullOrWhiteSpace(settings.TargetBaseUrl))
            return BadRequest(new { message = "Önce hedef adresi (targetBaseUrl) ayarlayın." });

        // Bearer token'ı yazdırılabilir ASCII olmalı: boşluk/denetim karakteri üstbilgi enjeksiyonuna yol açar
        var token = dto.Token.Trim();
        if (token.Length == 0 || token.Any(c => c < 0x21 || c > 0x7E))
            return BadRequest(new { message = "Token yalnızca boşluksuz, yazdırılabilir ASCII karakterler içermeli." });

        settings.TargetBearerTokenProtected = _secrets.ProtectToken(token);
        await _audit.AppendAsync(projectId, userId, AuditActions.TargetTokenSet, null, $"target={HostOf(settings.TargetBaseUrl)}");
        await _context.SaveChangesAsync();

        return Ok(new { targetTokenConfigured = true });
    }

    /// <summary>Hedef API token'ını siler.</summary>
    [HttpDelete("{projectId}/target-token")]
    public async Task<IActionResult> ClearTargetToken([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings.FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (settings is not null && settings.TargetBearerTokenProtected is not null)
        {
            settings.TargetBearerTokenProtected = null;
            await _audit.AppendAsync(projectId, userId, AuditActions.TargetTokenCleared, null, $"target={HostOf(settings.TargetBaseUrl)}");
            await _context.SaveChangesAsync();
        }

        return Ok(new { targetTokenConfigured = false });
    }

    /// <summary>
    /// Üretilmiş test senaryolarını hedef API'ye karşı çalıştırmak üzere kuyruğa alır (202). Koşuyu arka plan işçisi
    /// yürütür; sonuç GET test-runs ile okunur. Projede aynı anda yalnızca bir aktif koşu olabilir (409).
    /// </summary>
    [HttpPost("{projectId}/test-runs")]
    public async Task<IActionResult> StartTestRun([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId);
        if (string.IsNullOrWhiteSpace(settings?.TargetBaseUrl))
            return BadRequest(new { message = "Önce hedef adresi (targetBaseUrl) ayarlayın." });

        if (!await _context.TestScenarios.AnyAsync(s => s.Endpoint.ProjectId == projectId))
            return BadRequest(new { message = "Bu projede çalıştırılacak test senaryosu yok." });

        var conflict = new { message = "Bu proje için zaten bekleyen veya çalışan bir test koşusu var." };
        if (await _context.TestRuns.AnyAsync(r => r.ProjectId == projectId && TestRun.ActiveStatuses.Contains(r.Status)))
            return Conflict(conflict);

        var run = new TestRun { ProjectId = projectId, Status = TestRun.StatusPending, CreatedAt = DateTime.UtcNow };
        _context.TestRuns.Add(run);
        try
        {
            await _context.SaveChangesAsync();
            await _audit.AppendAsync(projectId, userId, AuditActions.TestRunStarted, $"run:{run.Id}", $"target={HostOf(settings!.TargetBaseUrl)}");
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // İki eşzamanlı istek yarıştı: benzersiz "aktif koşu" dizini ikincisini reddetti
            return Conflict(conflict);
        }

        return Accepted($"/api/automation/{projectId}/test-runs/{run.Id}", ToDto(run));
    }

    /// <summary>Projenin test koşularını (en yeni önce, en fazla son 20) listeler.</summary>
    [HttpGet("{projectId}/test-runs")]
    public async Task<IActionResult> GetTestRuns([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var runs = await _context.TestRuns
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.Id)
            .Take(TestRunProcessor.RunsToKeepPerProject)
            .ToListAsync();

        return Ok(runs.Select(ToDto));
    }

    /// <summary>Bir test koşusunu ve senaryo sonuçlarını döndürür.</summary>
    [HttpGet("{projectId}/test-runs/{runId}")]
    public async Task<IActionResult> GetTestRun([FromRoute] int projectId, [FromRoute] int runId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var run = await _context.TestRuns
            .AsNoTracking()
            .Include(r => r.Results)
            .FirstOrDefaultAsync(r => r.Id == runId && r.ProjectId == projectId);
        if (run is null)
            return NotFound(new { message = "Test koşusu bulunamadı." });

        var detail = new TestRunDetailDto
        {
            Id = run.Id,
            Status = run.Status,
            CreatedAt = run.CreatedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            Total = run.Total,
            Passed = run.Passed,
            Failed = run.Failed,
            Skipped = run.Skipped,
            TargetHost = run.TargetHost,
            Error = run.Error,
            Results = run.Results
                .OrderBy(x => x.Id)
                .Select(x => new TestRunResultDto
                {
                    Title = x.Title,
                    Method = x.Method,
                    Path = x.Path,
                    RequestPath = x.RequestPath,
                    ExpectedStatusCode = x.ExpectedStatusCode,
                    ActualStatusCode = x.ActualStatusCode,
                    Outcome = x.Outcome,
                    DurationMs = x.DurationMs,
                    Reason = x.Reason
                })
                .ToList()
        };

        return Ok(detail);
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
        await _audit.AppendAsync(projectId, userId, AuditActions.WebhookSecretRotated, null, $"webhook={HostOf(settings.WebhookUrl)}");
        await _context.SaveChangesAsync();

        return Ok(new { webhookSecret = secret });
    }

    /// <summary>
    /// Yapılandırılmış kanallara gerçek bir uyarı gibi ama "TEST" kuralıyla bir bildirim gönderir ve sonucu hemen döndürür.
    /// Webhook veya Telegram kurulumunu denemek içindir; uyarı kaydı (Alert) oluşturmaz.
    /// </summary>
    [HttpPost("{projectId}/test-notification")]
    public async Task<IActionResult> SendTestNotification([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var settings = await _context.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId);

        var hasWebhook = !string.IsNullOrWhiteSpace(settings?.WebhookUrl);
        var hasTelegram = settings?.NotifyTelegram == true;
        if (!hasWebhook && !hasTelegram)
            return BadRequest(new { message = "Önce bir bildirim kanalı ayarlayın (webhook adresi veya Telegram)." });

        var alert = new AlertRaised(
            projectId, "TEST", "Bu bir test bildirimidir; gerçek bir uyarı değildir.", "Info", DateTime.UtcNow, Guid.NewGuid());
        _events.Publish(alert);
        await _context.SaveChangesAsync();

        // Teslimat satırlarını aç ve gönder; sonucu beklemeden dönmek yerine hemen göster
        await _dispatcher.DispatchPendingAsync(HttpContext.RequestAborted);
        await _deliveries.ProcessDueAsync(HttpContext.RequestAborted);

        var results = await _context.NotificationDeliveries
            .AsNoTracking()
            .Where(d => d.EventId == alert.EventId)
            .OrderBy(d => d.Id)
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

        return Ok(new { eventId = alert.EventId, deliveries = results });
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

    private static string Flag(bool value) => value ? "on" : "off";

    /// <summary>Denetim kaydı için adresin yalnızca ana bilgisayarı (yol, sorgu, parola, token içermez); yoksa "-".</summary>
    private static string HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "-";

    private Task<bool> OwnsProjectAsync(int projectId, int userId) =>
        _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);

    private AutomationSettingsResponseDto ToDto(ProjectAutomationSettings settings) => new()
    {
        ScoreThreshold = settings.ScoreThreshold,
        NotifyOnMissingAuth = settings.NotifyOnMissingAuth,
        Enabled = settings.Enabled,
        NotifyTelegram = settings.NotifyTelegram,
        WebhookUrl = settings.WebhookUrl,
        WebhookSecretConfigured = !string.IsNullOrEmpty(settings.WebhookSecretProtected),
        TelegramAvailable = _telegramOptions.IsConfigured,
        TargetBaseUrl = settings.TargetBaseUrl,
        AllowMutatingTests = settings.AllowMutatingTests,
        TestFailureThresholdPercent = settings.TestFailureThresholdPercent,
        TargetTokenConfigured = !string.IsNullOrEmpty(settings.TargetBearerTokenProtected)
    };

    private static TestRunDto ToDto(TestRun run) => new()
    {
        Id = run.Id,
        Status = run.Status,
        CreatedAt = run.CreatedAt,
        StartedAt = run.StartedAt,
        CompletedAt = run.CompletedAt,
        Total = run.Total,
        Passed = run.Passed,
        Failed = run.Failed,
        Skipped = run.Skipped,
        TargetHost = run.TargetHost,
        Error = run.Error
    };

    /// <summary>İki adres aynı şema, ana bilgisayar ve porta mı sahip? (Hedef token'ının yeni adrese taşınıp taşınmayacağına karar verir.)</summary>
    private static bool SameOrigin(string? previous, string next)
    {
        if (!Uri.TryCreate(previous, UriKind.Absolute, out var a) || !Uri.TryCreate(next, UriKind.Absolute, out var b))
            return false;

        return a.Scheme == b.Scheme
               && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
               && a.Port == b.Port;
    }
}
