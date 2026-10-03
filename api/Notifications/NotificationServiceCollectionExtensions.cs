using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Telegram kanalını kaydeder. Bot token'ı istek adresinin içinde olduğundan, IHttpClientFactory'nin
    /// varsayılan günlükleri (istek adresini yazar) bu istemci için kapatılır; aksi halde token günlüğe sızar.
    /// </summary>
    public static IServiceCollection AddTelegramChannel(this IServiceCollection services)
    {
        services.AddOptions<TelegramOptions>();
        services.AddLogging(logging =>
            logging.AddFilter($"System.Net.Http.HttpClient.{TelegramChannel.HttpClientName}", LogLevel.None));

        services.AddHttpClient(TelegramChannel.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            });

        services.AddScoped<INotificationChannel, TelegramChannel>();
        return services;
    }
}
