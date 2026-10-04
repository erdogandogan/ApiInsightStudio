using System.Security.Cryptography;
using System.Text;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Audit;

/// <summary>Zincir doğrulamasının sonucu.</summary>
public sealed record AuditVerification(bool Valid, int EntryCount, int? FirstInvalidSequence, string? Reason, string? HeadHash);

/// <summary>
/// Ekleme-yalnız denetim izi. Kayıt, çağıranın iş değişikliğiyle aynı SaveChanges içinde yazılır (ya ikisi birden
/// kaydedilir ya da hiçbiri). Her kayıt bir öncekinin özetini taşır (SHA-256 zinciri). Zincir yalnızca DEĞİŞTİRME,
/// SİLME ve ARAYA EKLEME girişimlerini ortaya çıkarır; veritabanına tam yetkisi olan biri zinciri baştan hesaplayabilir
/// ve en sondaki kayıtların silinmesi tek başına anlaşılamaz (bu yüzden doğrulama son özeti de döndürür).
/// </summary>
public class AuditTrail
{
    private const int VerifyBatchSize = 500;

    private readonly AppDbContext _context;
    private readonly TimeProvider _time;

    public AuditTrail(AppDbContext context, TimeProvider time)
    {
        _context = context;
        _time = time;
    }

    /// <summary>
    /// Bir kaydı bağlama ekler; kaydetmek çağıranın işidir. <paramref name="details"/> gizli bilgi içermemeli
    /// (token, sır, tam adres yok); uzunsa kısaltılır.
    /// </summary>
    public async Task AppendAsync(
        int projectId, int? actorUserId, string action, string? subject, string details, CancellationToken cancellationToken = default)
    {
        // Aynı SaveChanges içinde birden çok kayıt eklenebilir: sırayı önce bağlamdaki bekleyen kayıtlardan al.
        var pending = _context.ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Added && e.Entity.ProjectId == projectId)
            .Select(e => e.Entity)
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefault();

        int sequence;
        string prevHash;
        if (pending is not null)
        {
            sequence = pending.Sequence + 1;
            prevHash = pending.Hash;
        }
        else
        {
            var last = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.ProjectId == projectId)
                .OrderByDescending(a => a.Sequence)
                .Select(a => new { a.Sequence, a.Hash })
                .FirstOrDefaultAsync(cancellationToken);
            sequence = (last?.Sequence ?? 0) + 1;
            prevHash = last?.Hash ?? AuditLog.GenesisHash;
        }

        var entry = new AuditLog
        {
            ProjectId = projectId,
            Sequence = sequence,
            OccurredAt = _time.GetUtcNow().UtcDateTime,
            ActorUserId = actorUserId,
            Action = action,
            Subject = subject,
            Details = Truncate(details, AuditLog.MaxDetailsLength),
            PrevHash = prevHash
        };
        entry.Hash = ComputeHash(entry);
        _context.AuditLogs.Add(entry);
    }

    /// <summary>Projenin zincirini baştan sona doğrular.</summary>
    public async Task<AuditVerification> VerifyAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var expectedSequence = 1;
        var expectedPrev = AuditLog.GenesisHash;
        var lastSeq = 0;

        while (true)
        {
            var batch = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.ProjectId == projectId && a.Sequence > lastSeq)
                .OrderBy(a => a.Sequence)
                .Take(VerifyBatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                if (entry.Sequence != expectedSequence)
                    return Invalid(expectedSequence - 1, entry.Sequence, "Sıra numarasında boşluk var (kayıt silinmiş olabilir).");
                if (entry.PrevHash != expectedPrev)
                    return Invalid(expectedSequence - 1, entry.Sequence, "Önceki kaydın özeti eşleşmiyor.");
                if (!string.Equals(entry.Hash, ComputeHash(entry), StringComparison.Ordinal))
                    return Invalid(expectedSequence - 1, entry.Sequence, "Kayıt içeriği özetiyle uyuşmuyor (kayıt değiştirilmiş).");

                expectedPrev = entry.Hash;
                expectedSequence++;
                lastSeq = entry.Sequence;
            }
        }

        var count = expectedSequence - 1;
        return new AuditVerification(true, count, null, null, count == 0 ? null : expectedPrev);

        static AuditVerification Invalid(int validCount, int sequence, string reason) =>
            new(false, validCount, sequence, reason, null);
    }

    /// <summary>Kaydın özeti: alanlar uzunluk önekli sabit biçimde birleştirilip (önceki özet dahil) SHA-256 alınır.</summary>
    public static string ComputeHash(AuditLog entry)
    {
        // Her alan uzunluk önekiyle yazılır: alan içeriği (örn. satır sonu) alan sınırlarını kaydıramaz.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var fields = new string?[]
        {
            entry.ProjectId.ToString(inv),
            entry.Sequence.ToString(inv),
            entry.OccurredAt.Ticks.ToString(inv),
            entry.ActorUserId?.ToString(inv),
            entry.Action,
            entry.Subject,
            entry.Details,
            entry.PrevHash
        };
        // Boş değer ("N;") ile "-" gibi gerçek bir metin birbirine karışmaz.
        var canonical = string.Concat(fields.Select(f => f is null ? "N;" : $"{f.Length}:{f};"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
