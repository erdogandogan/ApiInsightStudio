using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>
/// Bir uyarı olayının bir kanala teslimatı. Her uyarı ve kanal için tek satır tutulur; bir kanal başarısız
/// olunca yalnızca o kanal yeniden denenir, diğer kanallara mükerrer mesaj gitmez.
/// </summary>
public class NotificationDelivery
{
    public const string StatusPending = "Pending";
    public const string StatusSucceeded = "Succeeded";
    public const string StatusDead = "Dead";

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>Uyarı olayının benzersiz kimliği; alıcıya X-Event-Id olarak gider (tekrarı ayıklamak için).</summary>
    public Guid EventId { get; set; }

    /// <summary>Kanal adı (örn. webhook, telegram).</summary>
    public string Channel { get; set; } = string.Empty;

    public string Status { get; set; } = StatusPending;

    public int Attempts { get; set; }

    /// <summary>Bu zamandan önce yeniden denenmez; null ise hemen denenebilir.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public int? LastStatusCode { get; set; }

    /// <summary>Son hatanın kısa metni. Adres, token veya sır içermez.</summary>
    public string? LastError { get; set; }

    /// <summary>Gönderilecek uyarı olayının JSON kopyası (teslimat kendi başına yeterli olsun diye).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public Project Project { get; set; } = null!;
}
