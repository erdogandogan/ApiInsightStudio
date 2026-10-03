using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Zamanı gelmiş teslimatları ilgili kanaldan gönderir. Başarısızlıkta üssel geri çekilmeyle yeniden dener
/// (<see cref="RetryPolicy"/>), hak bitince teslimatı "ölü" yapar. Kalıcı hatalar (adres kaldırılmış, güvensiz,
/// sır yok) hiç yeniden denenmeden ölü olur.
/// </summary>
public class DeliveryProcessor
{
    private const int BatchSize = 20;
    private const int MaxErrorLength = 300;

    // Tek sunucu varsayımı: aynı teslimatın iki kez gönderilmemesi için süreç genelinde tek işleyici çalışır.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _dbContext;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly TimeProvider _time;
    private readonly ILogger<DeliveryProcessor> _logger;

    public DeliveryProcessor(
        AppDbContext dbContext,
        IEnumerable<INotificationChannel> channels,
        TimeProvider time,
        ILogger<DeliveryProcessor> logger)
    {
        _dbContext = dbContext;
        _channels = channels;
        _time = time;
        _logger = logger;
    }

    /// <summary>İşlenen (denenen) teslimat sayısını döndürür. İstisna fırlatmaz.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await ProcessCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teslimatlar işlenirken beklenmeyen hata oluştu.");
            return 0;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<int> ProcessCoreAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var due = await _dbContext.NotificationDeliveries
            .Where(d => d.Status == NotificationDelivery.StatusPending
                        && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var delivery in due)
        {
            var result = await TrySendAsync(delivery, cancellationToken);
            Apply(delivery, result, _time.GetUtcNow().UtcDateTime);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task<DeliveryResult> TrySendAsync(NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            var channel = _channels.FirstOrDefault(c => c.Name == delivery.Channel);
            if (channel is null)
                return DeliveryResult.Permanent($"Bilinmeyen bildirim kanalı: {delivery.Channel}.");

            var alert = JsonSerializer.Deserialize<AlertRaised>(delivery.PayloadJson);
            if (alert is null)
                return DeliveryResult.Permanent("Teslimat içeriği okunamadı.");

            var projectName = await _dbContext.Projects
                .AsNoTracking()
                .Where(p => p.Id == delivery.ProjectId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (projectName is null)
                return DeliveryResult.Permanent("Proje bulunamadı.");

            var settings = await _dbContext.ProjectAutomationSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProjectId == delivery.ProjectId, cancellationToken)
                ?? new ProjectAutomationSettings { ProjectId = delivery.ProjectId };

            return await channel.SendAsync(
                new NotificationContext(delivery.ProjectId, projectName, alert, settings), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teslimat {DeliveryId} ({Channel}) gönderilirken beklenmeyen hata.", delivery.Id, delivery.Channel);
            return DeliveryResult.Failed(null, $"Beklenmeyen hata: {ex.GetType().Name}.");
        }
    }

    private static void Apply(NotificationDelivery delivery, DeliveryResult result, DateTime now)
    {
        delivery.Attempts++;
        delivery.LastStatusCode = result.StatusCode;

        if (result.Success)
        {
            delivery.Status = NotificationDelivery.StatusSucceeded;
            delivery.LastError = null;
            delivery.NextAttemptAt = null;
            delivery.CompletedAt = now;
            return;
        }

        delivery.LastError = Truncate(result.Error ?? "Bilinmeyen hata.");

        var delay = result.IsPermanent ? null : RetryPolicy.NextDelay(delivery.Attempts);
        if (delay is null)
        {
            delivery.Status = NotificationDelivery.StatusDead;
            delivery.NextAttemptAt = null;
            delivery.CompletedAt = now;
        }
        else
        {
            delivery.NextAttemptAt = now + delay.Value;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
