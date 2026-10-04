using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>
/// Projedeki bir eylemin ekleme-yalnız kaydı. Her kayıt, bir öncekinin özetini (hash) taşır; böylece sonradan
/// değiştirilen, silinen veya araya eklenen bir kayıt zincir doğrulamasında yakalanır. Gizli bilgi (token, sır,
/// tam adres) içermez.
/// </summary>
public class AuditLog
{
    /// <summary>İlk kaydın önceki özeti.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public const int MaxDetailsLength = 500;

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    /// <summary>Projedeki kayıt sırası: 1'den başlar, boşluksuz artar.</summary>
    public int Sequence { get; set; }

    public DateTime OccurredAt { get; set; }

    /// <summary>Eylemi yapan kullanıcı; sistem (arka plan işçisi) eylemlerinde boştur.</summary>
    public int? ActorUserId { get; set; }

    [MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Eylemin konusu (örn. "suggestion:5", "run:3"); yoksa boş.</summary>
    [MaxLength(64)]
    public string? Subject { get; set; }

    [MaxLength(MaxDetailsLength)]
    public string Details { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PrevHash { get; set; } = GenesisHash;

    [MaxLength(64)]
    public string Hash { get; set; } = string.Empty;

    public Project Project { get; set; } = null!;
}
