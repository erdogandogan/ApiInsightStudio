using Microsoft.Extensions.Options;
using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.TestRunner;

/// <summary>
/// Test koşusu kuyruğunu ayrı bir döngüde işler: uzun süren bir koşu, bildirim/olay işçisini
/// (<see cref="AutomationWorker"/>) bekletmesin diye ayrı bir BackgroundService'tir.
/// </summary>
public class TestRunWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<TestRunWorker> _logger;

    public TestRunWorker(IServiceScopeFactory scopeFactory, IOptions<NotificationOptions> options, ILogger<TestRunWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollSeconds));
        _logger.LogInformation("Test koşusu işçisi başladı (aralık: {Seconds} sn).", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<TestRunProcessor>().ProcessPendingAsync(stoppingToken);

                // Kuyrukta başka koşu varsa beklemeden devam et
                if (processed > 0)
                    continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Test koşusu işçisi turu başarısız oldu: {ExceptionType}.", ex.GetType().Name);
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
