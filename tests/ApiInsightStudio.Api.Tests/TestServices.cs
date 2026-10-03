using ApiInsightStudio.Api.Automation;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Uygulamadaki DI bağlantısını testlerde, verilen tek bir DbContext üzerinde yeniden kurar.</summary>
public static class TestServices
{
    public static ServiceProvider BuildProvider(AppDbContext context, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
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
