using ApiInsightStudio.Api.Audit;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.DTOs;
using ApiInsightStudio.Api.Extensions;
using ApiInsightStudio.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Controllers;

/// <summary>
/// AI önerilerinin insan incelemesi: listele, onayla (isteğe bağlı düzenleyerek) veya reddet. Yalnızca onaylanan metin
/// <see cref="Models.Endpoint.AiSummary"/> alanına yazılır; skor ve uyarılar etkilenmez.
/// </summary>
[ApiController]
[Route("api/automation")]
[Authorize]
public class AiReviewController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AuditTrail _audit;
    private readonly TimeProvider _time;

    public AiReviewController(AppDbContext context, AuditTrail audit, TimeProvider time)
    {
        _context = context;
        _audit = audit;
        _time = time;
    }

    /// <summary>Projenin AI önerilerini (en yeni önce) listeler. İsteğe bağlı status=Pending|Approved|Rejected|Superseded.</summary>
    [HttpGet("{projectId}/ai-suggestions")]
    public async Task<IActionResult> List([FromRoute] int projectId, [FromQuery] string? status = null, [FromQuery] int take = 100)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        if (status is not null && !IsKnownStatus(status))
            return BadRequest(new { message = "status yalnızca Pending, Approved, Rejected veya Superseded olabilir." });

        take = Math.Clamp(take, 1, 200);

        var query = _context.AiSuggestions.AsNoTracking().Where(s => s.ProjectId == projectId);
        if (status is not null)
            query = query.Where(s => s.Status == status);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Take(take)
            .ToListAsync();

        return Ok(items.Select(ToDto));
    }

    /// <summary>Bekleyen bir öneriyi onaylar: (varsa düzenlenmiş) metin uç noktanın AiSummary alanına yazılır.</summary>
    [HttpPost("{projectId}/ai-suggestions/{suggestionId}/approve")]
    public async Task<IActionResult> Approve(
        [FromRoute] int projectId, [FromRoute] int suggestionId, [FromBody] ApproveSuggestionDto? dto)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var suggestion = await _context.AiSuggestions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == suggestionId && s.ProjectId == projectId);
        if (suggestion is null)
            return NotFound(new { message = "Öneri bulunamadı." });

        string? edited = null;
        if (dto?.EditedContent is not null)
        {
            edited = dto.EditedContent.Trim();
            if (edited.Length == 0)
                return BadRequest(new { message = "Düzenlenmiş metin boş olamaz (özgün metni onaylamak için editedContent göndermeyin)." });
        }

        var note = string.IsNullOrWhiteSpace(dto?.Note) ? null : dto!.Note!.Trim();
        var published = edited ?? suggestion.Content;
        var wasEdited = edited is not null && edited != suggestion.Content;
        string? finalContent = wasEdited ? published : null;
        var now = _time.GetUtcNow().UtcDateTime;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Atomik geçiş: yalnızca hâlâ Pending ise. İki eşzamanlı onay/ret yarışsa yalnızca biri kazanır.
        var changed = await _context.AiSuggestions
            .Where(s => s.Id == suggestionId && s.ProjectId == projectId && s.Status == AiSuggestion.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, AiSuggestion.StatusApproved)
                .SetProperty(x => x.FinalContent, finalContent)
                .SetProperty(x => x.ReviewedByUserId, userId)
                .SetProperty(x => x.ReviewedAt, now)
                .SetProperty(x => x.ReviewNote, note));
        if (changed == 0)
            return Conflict(new { message = "Bu öneri artık onay beklemiyor (zaten incelenmiş veya yenisiyle değiştirilmiş)." });

        var endpointChanged = await _context.Endpoints
            .Where(e => e.Id == suggestion.EndpointId && e.ProjectId == projectId)
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.AiSummary, published));
        if (endpointChanged == 0)
        {
            // Uç nokta artık yok: transaction commit edilmeden çıkıldığı için onay geri sarılır.
            return Conflict(new { message = "Önerinin uç noktası artık yok." });
        }

        await _audit.AppendAsync(projectId, userId, AuditActions.SuggestionApproved, $"suggestion:{suggestionId}",
            $"endpoint={suggestion.Method} {suggestion.Path}; edited={(wasEdited ? "true" : "false")}; model={suggestion.Model}; note={(note is null ? "no" : "yes")}");
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var updated = await _context.AiSuggestions.AsNoTracking().FirstAsync(s => s.Id == suggestionId);
        return Ok(ToDto(updated));
    }

    /// <summary>Bekleyen bir öneriyi reddeder; hiçbir yere yazılmaz.</summary>
    [HttpPost("{projectId}/ai-suggestions/{suggestionId}/reject")]
    public async Task<IActionResult> Reject(
        [FromRoute] int projectId, [FromRoute] int suggestionId, [FromBody] RejectSuggestionDto? dto)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized(new { message = "Geçerli bir kullanıcı bilgisi bulunamadı." });

        if (!await OwnsProjectAsync(projectId, userId))
            return NotFound(new { message = "Proje bulunamadı." });

        var suggestion = await _context.AiSuggestions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == suggestionId && s.ProjectId == projectId);
        if (suggestion is null)
            return NotFound(new { message = "Öneri bulunamadı." });

        var note = string.IsNullOrWhiteSpace(dto?.Note) ? null : dto!.Note!.Trim();
        var now = _time.GetUtcNow().UtcDateTime;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var changed = await _context.AiSuggestions
            .Where(s => s.Id == suggestionId && s.ProjectId == projectId && s.Status == AiSuggestion.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, AiSuggestion.StatusRejected)
                .SetProperty(x => x.ReviewedByUserId, userId)
                .SetProperty(x => x.ReviewedAt, now)
                .SetProperty(x => x.ReviewNote, note));
        if (changed == 0)
            return Conflict(new { message = "Bu öneri artık onay beklemiyor (zaten incelenmiş veya yenisiyle değiştirilmiş)." });

        await _audit.AppendAsync(projectId, userId, AuditActions.SuggestionRejected, $"suggestion:{suggestionId}",
            $"endpoint={suggestion.Method} {suggestion.Path}; model={suggestion.Model}; note={(note is null ? "no" : "yes")}");
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        var updated = await _context.AiSuggestions.AsNoTracking().FirstAsync(s => s.Id == suggestionId);
        return Ok(ToDto(updated));
    }

    private static bool IsKnownStatus(string status) =>
        status is AiSuggestion.StatusPending or AiSuggestion.StatusApproved
            or AiSuggestion.StatusRejected or AiSuggestion.StatusSuperseded;

    private Task<bool> OwnsProjectAsync(int projectId, int userId) =>
        _context.Projects.AnyAsync(p => p.Id == projectId && p.UserId == userId);

    internal static AiSuggestionDto ToDto(AiSuggestion s) => new()
    {
        Id = s.Id,
        EndpointId = s.EndpointId,
        Method = s.Method,
        Path = s.Path,
        Content = s.Content,
        FinalContent = s.FinalContent,
        Model = s.Model,
        PromptVersion = s.PromptVersion,
        Status = s.Status,
        CreatedAt = s.CreatedAt,
        ReviewedAt = s.ReviewedAt,
        ReviewNote = s.ReviewNote
    };
}
