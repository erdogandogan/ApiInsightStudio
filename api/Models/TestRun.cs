using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiInsightStudio.Api.Models;

/// <summary>Bir projenin otomatik test koşusu: kuyruğa alınır, arka plan işçisi çalıştırır, sonuçlar <see cref="TestRunResult"/> satırlarında tutulur.</summary>
public class TestRun
{
    public const string StatusPending = "Pending";
    public const string StatusRunning = "Running";
    public const string StatusCompleted = "Completed";
    public const string StatusFailed = "Failed";

    /// <summary>Bir projede aynı anda en fazla bir koşu Pending/Running olabilir.</summary>
    public static readonly string[] ActiveStatuses = { StatusPending, StatusRunning };

    [Key]
    public int Id { get; set; }

    [ForeignKey(nameof(Project))]
    public int ProjectId { get; set; }

    public string Status { get; set; } = StatusPending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int Total { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    /// <summary>Koşunun hedef ana bilgisayarı (yalnızca gösterim; yol, sorgu ve token içermez).</summary>
    public string? TargetHost { get; set; }

    /// <summary>Koşu Failed ise nedeni (hassas veri içermez).</summary>
    public string? Error { get; set; }

    public Project Project { get; set; } = null!;

    public ICollection<TestRunResult> Results { get; set; } = new List<TestRunResult>();
}
