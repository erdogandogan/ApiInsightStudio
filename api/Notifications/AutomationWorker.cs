using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Events;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Arka plan işçisi: belirli aralıklarla bekleyen olayları (outbox) ve zamanı gelmiş teslimatları işler.
/// Bir tur hata verse bile işçi durmaz; hatalar kaydedilir ve bir sonraki turda devam edilir.
/// </summary>
public class AutomationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<AutomationWorker> _logger;

    public AutomationWorker(
        IServiceScopeFactory scopeFactory, IOptions<NotificationOptions> options, ILogger<AutomationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollSeconds));
        _logger.LogInformation("Otomasyon işçisi başladı (aralık: {Seconds} sn).", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                // Zamana bağlı kural: bekleyen AI önerisi hatırlatması (uyarı açılırsa aşağıdaki dağıtımla hemen bildirilir)
                await scope.ServiceProvider.GetRequiredService<ReviewReminderService>().EvaluateAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync(stoppingToken);
                await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Otomasyon işçisi turu başarısız oldu; sonraki turda devam edilecek.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
