using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>
/// Bir uç nokta için yapay zekâ tarafından üretilen açıklama taslağı. Taslak, bir insan onaylayana kadar
/// <see cref="Endpoint.AiSummary"/> alanına yazılmaz. Hangi modelin ve hangi komut sürümüyle ürettiği kayıtlıdır.
/// </summary>
public class AiSuggestion
{
    public const string StatusPending = "Pending";
    public const string StatusApproved = "Approved";
    public const string StatusRejected = "Rejected";

    /// <summary>Aynı uç nokta için daha yeni bir öneri üretildiği için geçersiz kalan, hiç incelenmemiş öneri.</summary>
    public const string StatusSuperseded = "Superseded";

    public const int MaxContentLength = 2000;
    public const int MaxNoteLength = 500;

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>
    /// Uç nokta kimliği. Bilerek yabancı anahtar değildir: projeyi silme akışı uç noktaları elle siler ve
    /// ikinci bir cascade yolu açılmasın; öneri proje silinince projeyle birlikte gider.
    /// </summary>
    public int EndpointId { get; set; }

    /// <summary>Üretim anındaki metot ve yol (uç nokta sonradan değişse de kayıt anlaşılır kalır).</summary>
    [MaxLength(16)]
    public string Method { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Modelin ürettiği özgün metin; asla değiştirilmez.</summary>
    [MaxLength(MaxContentLength)]
    public string Content { get; set; } = string.Empty;

    /// <summary>Onaylanırken kullanıcı metni düzenlediyse yayımlanan son metin; yoksa <see cref="Content"/> yayımlanmıştır.</summary>
    [MaxLength(MaxContentLength)]
    public string? FinalContent { get; set; }

    [MaxLength(200)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PromptVersion { get; set; } = string.Empty;

    [MaxLength(16)]
    public string Status { get; set; } = StatusPending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CreatedByUserId { get; set; }

    public int? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(MaxNoteLength)]
    public string? ReviewNote { get; set; }

    public Project Project { get; set; } = null!;
}
