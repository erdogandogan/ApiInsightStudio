using System.ComponentModel.DataAnnotations;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.DTOs;

public class AiSuggestionDto
{
    public int Id { get; set; }

    public int EndpointId { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Onaylanırken düzenlendiyse yayımlanan son metin; düzenlenmediyse boş.</summary>
    public string? FinalContent { get; set; }

    public string Model { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? ReviewNote { get; set; }
}

public class ApproveSuggestionDto
{
    /// <summary>İsteğe bağlı: metni düzenleyerek onayla. Boşsa modelin özgün metni yayımlanır.</summary>
    [StringLength(AiSuggestion.MaxContentLength)]
    public string? EditedContent { get; set; }

    [StringLength(AiSuggestion.MaxNoteLength)]
    public string? Note { get; set; }
}

public class RejectSuggestionDto
{
    [StringLength(AiSuggestion.MaxNoteLength)]
    public string? Note { get; set; }
}

public class AuditEntryDto
{
    public int Sequence { get; set; }

    public DateTime OccurredAt { get; set; }

    public int? ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string Details { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;
}

public class AuditPageDto
{
    public List<AuditEntryDto> Items { get; set; } = new();

    /// <summary>Sonraki (daha eski) sayfa için "before" değeri; sayfa sonuysa boş.</summary>
    public int? NextBefore { get; set; }
}
