using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ApiInsightStudio.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Events;

/// <summary>
/// Outbox'taki bekleyen olayları, kayıtlı işleyicilere verir. Şimdilik analiz biter bitmez çağrılır;
/// Aşama 2'de aynı metodu bir arka plan servisi çağıracak.
/// </summary>
public class OutboxDispatcher
{
    /// <summary>Bu kadar başarısız denemeden sonra mesaj bir daha denenmez.</summary>
    public const int MaxAttempts = 5;

    private const int BatchSize = 50;
    private const int MaxErrorLength = 500;

    private readonly AppDbContext _dbContext;
    private readonly IServiceProvider _services;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(AppDbContext dbContext, IServiceProvider services, ILogger<OutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _services = services;
        _logger = logger;
    }

    /// <summary>İşlenen mesaj sayısını döndürür. Hiçbir koşulda istisna fırlatmaz: analiz isteğini bozmamalı.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await DispatchCoreAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox işlenirken beklenmeyen hata oluştu.");
            return 0;
        }
    }

    private async Task<int> DispatchCoreAsync(CancellationToken cancellationToken)
    {
        // İşleyicisi olmayan türler (şimdilik AlertRaised) beklemede kalır; Aşama 2'de işleyicisi gelince işlenir.
        var handledTypeNames = EventTypes.All
            .Where(pair => GetHandlers(pair.Value).Count > 0)
            .Select(pair => pair.Key)
            .ToList();

        if (handledTypeNames.Count == 0)
            return 0;

        var pending = await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts && handledTypeNames.Contains(m.Type))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .Select(m => new { m.Id, m.Type, m.PayloadJson })
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

                var processedAt = DateTime.UtcNow;
                await _dbContext.OutboxMessages
                    .Where(m => m.Id == message.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, processedAt), cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox mesajı {MessageId} ({Type}) işlenemedi.", message.Id, message.Type);

                // Başarısız işleyicinin yarım kalan değişiklikleri atılır; yalnızca deneme sayısı ve hata kaydedilir.
                _dbContext.ChangeTracker.Clear();
                var error = Truncate(ex.Message);
                await _dbContext.OutboxMessages
                    .Where(m => m.Id == message.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Attempts, m => m.Attempts + 1)
                        .SetProperty(m => m.LastError, error), cancellationToken);
            }
        }

        return processed;
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
