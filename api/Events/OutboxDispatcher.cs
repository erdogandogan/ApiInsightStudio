using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Events;

/// <summary>
/// Outbox'taki bekleyen olayları, kayıtlı işleyicilere verir. Analiz biter bitmez (hemen) ve arka plan
/// işçisi tarafından (<see cref="AutomationWorker"/>) çağrılır. Başarısız mesajlar üssel geri çekilmeyle
/// yeniden denenir (<see cref="RetryPolicy"/>), hak bitince "ölü" olur.
/// </summary>
public class OutboxDispatcher
{
    /// <summary>Bir çağrıda, bir olayın tetiklediği zincir (örn. AnalysisCompleted → AlertRaised) için en fazla tur sayısı.</summary>
    private const int MaxRounds = 5;

    private const int BatchSize = 50;
    private const int MaxErrorLength = 500;

    // Tek sunucu varsayımı: aynı mesajın iki kez işlenmemesi için süreç genelinde tek dispatcher çalışır.
    // Birden fazla sunucuya ölçeklenirse bunun yerine veritabanı düzeyinde bir kilit (kiralama) gerekir.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly AppDbContext _dbContext;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        AppDbContext dbContext, IServiceProvider services, TimeProvider time, ILogger<OutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _services = services;
        _time = time;
        _logger = logger;
    }

    /// <summary>İşlenen mesaj sayısını döndürür. Hiçbir koşulda istisna fırlatmaz: analiz isteğini bozmamalı.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        try
        {
            var total = 0;
            for (var round = 0; round < MaxRounds; round++)
            {
                var processed = await DispatchCoreAsync(cancellationToken);
                total += processed;
                if (processed == 0)
                    break;
            }

            return total;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox işlenirken beklenmeyen hata oluştu.");
            return 0;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<int> DispatchCoreAsync(CancellationToken cancellationToken)
    {
        // İşleyicisi olmayan türler beklemede kalır; işleyicisi gelince işlenir.
        var handledTypeNames = EventTypes.All
            .Where(pair => GetHandlers(pair.Value).Count > 0)
            .Select(pair => pair.Key)
            .ToList();

        if (handledTypeNames.Count == 0)
            return 0;

        var now = _time.GetUtcNow().UtcDateTime;

        var pending = await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null
                        && m.DeadAt == null
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now)
                        && handledTypeNames.Contains(m.Type))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .Select(m => new { m.Id, m.Type, m.PayloadJson, m.Attempts })
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var message in pending)
        {
            var eventType = EventTypes.All[message.Type];
            try
            {
                // İşleyicilerin değişiklikleri ve "işlendi" işareti tek işlemde kaydedilir.
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var @event = JsonSerializer.Deserialize(message.PayloadJson, eventType)
                    ?? throw new InvalidOperationException("Olay içeriği boş.");

                foreach (var handler in GetHandlers(eventType))
                    await InvokeHandlerAsync(handler, eventType, @event, cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);

                var processedAt = _time.GetUtcNow().UtcDateTime;
                await _dbContext.OutboxMessages
                    .Where(m => m.Id == message.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, processedAt), cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Outbox mesajı {MessageId} ({Type}) işlenemedi.", message.Id, message.Type);

                // Başarısız işleyicinin yarım kalan değişiklikleri atılır; yalnızca deneme sayısı, hata ve
                // bir sonraki deneme zamanı (veya ölü işareti) kaydedilir.
                _dbContext.ChangeTracker.Clear();
                await RecordFailureAsync(message.Id, message.Attempts + 1, ex, cancellationToken);
            }
        }

        return processed;
    }

    private async Task RecordFailureAsync(int messageId, int attempts, Exception ex, CancellationToken cancellationToken)
    {
        var failedAt = _time.GetUtcNow().UtcDateTime;
        var error = Truncate(ex.Message);
        var delay = RetryPolicy.NextDelay(attempts);

        DateTime? nextAttemptAt = delay is null ? null : failedAt + delay.Value;
        DateTime? deadAt = delay is null ? failedAt : null;

        await _dbContext.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.NextAttemptAt, nextAttemptAt)
                .SetProperty(m => m.DeadAt, deadAt), cancellationToken);
    }

    private List<object> GetHandlers(Type eventType)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        return _services.GetServices(handlerType).Where(h => h is not null).Cast<object>().ToList();
    }

    private static Task InvokeHandlerAsync(object handler, Type eventType, object @event, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var method = handlerType.GetMethod(nameof(IEventHandler<object>.HandleAsync))!;
        try
        {
            return (Task)method.Invoke(handler, new[] { @event, cancellationToken })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Yansıma, işleyicinin gerçek hatasını sarmalar; kayıtlara asıl hata yazılsın.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
