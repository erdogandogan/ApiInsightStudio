using ApiInsightStudio.Api.Audit;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Controllers;

/// <summary>Projenin denetim izini okur ve zincirini doğrular. Yalnızca okuma: güncelleme/silme uç noktası yoktur.</summary>
[ApiController]
[Route("api/automation")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AuditTrail _audit;

    public AuditController(AppDbContext context, AuditTrail audit)
    {
        _context = context;
        _audit = audit;
    }

    /// <summary>
    /// Kayıtları en yeniden eskiye listeler. before=&lt;sıra no&gt; ile bir önceki sayfaya geçilir; take en fazla 100;
    /// action=ai.suggestion gibi bir önek verilirse yalnızca o eylemler gelir.
    /// </summary>
    [HttpGet("{projectId}/audit")]
    public async Task<IActionResult> List(
        [FromRoute] int projectId, [FromQuery] int? before = null, [FromQuery] int take = 50, [FromQuery] string? action = null)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        take = Math.Clamp(take, 1, 100);

        var query = _context.AuditLogs.AsNoTracking().Where(a => a.ProjectId == projectId);
        if (before is not null)
            query = query.Where(a => a.Sequence < before);
        if (!string.IsNullOrWhiteSpace(action))
        {
            var prefix = action.Trim();
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        // Bir fazlasını çekerek "daha eski sayfa var mı" sorusunu cevapla
        var rows = await query
            .OrderByDescending(a => a.Sequence)
            .Take(take + 1)
            .Select(a => new AuditEntryDto
            {
                Sequence = a.Sequence,
                OccurredAt = a.OccurredAt,
                ActorUserId = a.ActorUserId,
                Action = a.Action,
                Subject = a.Subject,
                Details = a.Details,
                Hash = a.Hash
            })
            .ToListAsync();

        var page = new AuditPageDto { Items = rows.Take(take).ToList() };
        if (rows.Count > take)
            page.NextBefore = page.Items[^1].Sequence;

        return Ok(page);
    }

    /// <summary>Zinciri baştan sona doğrular; bozulma varsa ilk bozuk sıra numarasını söyler.</summary>
    [HttpGet("{projectId}/audit/verify")]
    public async Task<IActionResult> Verify([FromRoute] int projectId)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        return Ok(await _audit.VerifyAsync(projectId, HttpContext.RequestAborted));
    }

    private Task<bool> OwnsProjectAsync(int projectId, int userId) =>
        _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);
}
