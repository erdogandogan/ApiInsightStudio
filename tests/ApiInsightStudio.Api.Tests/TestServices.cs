using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Ayarlanabilir saat: geri çekilme (backoff) testlerinde zamanı gerçekten beklemeden ilerletmek için.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Uygulamadaki DI bağlantısını testlerde, verilen tek bir DbContext üzerinde yeniden kurar.</summary>
public static class TestServices
{
    public static ServiceProvider BuildProvider(
        AppDbContext context, Action<IServiceCollection>? configure = null, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddSingleton(time ?? TimeProvider.System);
        services.AddSingleton<AlertRuleEvaluator>();
        services.AddSingleton<IEventPublisher, OutboxEventPublisher>();
        services.AddSingleton<OutboxDispatcher>();

        if (configure is null)
            services.AddSingleton<IEventHandler<AnalysisCompleted>, AnalysisCompletedHandler>();
        else
            configure(services);

        return services.BuildServiceProvider();
    }

    /// <summary>Gerçek olay işleyicileriyle (kurallar çalışır) bir AnalysisService kurar.</summary>
    public static AnalysisService CreateAnalysisService(AppDbContext context) =>
        CreateAnalysisService(BuildProvider(context));

    public static AnalysisService CreateAnalysisService(ServiceProvider provider) =>
        new(provider.GetRequiredService<AppDbContext>(),
            provider.GetRequiredService<IEventPublisher>(),
            provider.GetRequiredService<OutboxDispatcher>());
}
