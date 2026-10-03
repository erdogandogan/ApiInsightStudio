using System.Text;
using System.Text.Json;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Projenin webhook adresine imzalı bir JSON POST gönderir. Başlıklar: X-Event-Id (tekrar ayıklama için),
/// X-Timestamp ve X-Signature (sha256=HMAC). 2xx dışındaki cevaplar ve yönlendirmeler başarısız sayılır.
/// </summary>
public class WebhookChannel : INotificationChannel
{
    public const string ChannelName = "webhook";
    public const string HttpClientName = "webhook";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WebhookSecretProtector _protector;
    private readonly TimeProvider _time;
    private readonly Microsoft.Extensions.Options.IOptions<NotificationOptions> _options;

    public WebhookChannel(
        IHttpClientFactory httpClientFactory,
        WebhookSecretProtector protector,
        TimeProvider time,
        Microsoft.Extensions.Options.IOptions<NotificationOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _time = time;
        _options = options;
    }

    public string Name => ChannelName;

    public async Task<DeliveryResult> SendAsync(NotificationContext context, CancellationToken cancellationToken)
    {
        var settings = context.Settings;

        // Kayıttan sonra ayar değişmiş olabilir; gönderim anında tekrar doğrula.
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
            return DeliveryResult.Permanent("Webhook adresi kaldırılmış.");

        var syntax = UrlSafety.ValidateSyntax(settings.WebhookUrl, _options.Value.AllowedPrivateHosts);
        if (!syntax.Ok)
            return DeliveryResult.Permanent($"Webhook adresi artık geçerli değil: {syntax.Error}");

        if (string.IsNullOrWhiteSpace(settings.WebhookSecretProtected))
            return DeliveryResult.Permanent("Webhook sırrı oluşturulmamış.");

        string secret;
        try
        {
            secret = _protector.Unprotect(settings.WebhookSecretProtected);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return DeliveryResult.Permanent("Webhook sırrı çözülemedi (anahtar halkası değişmiş olabilir); sırrı yenileyin.");
        }

        var body = JsonSerializer.Serialize(new
        {
            id = context.Alert.EventId,
            type = "alert.raised",
            createdAt = context.Alert.RaisedAt,
            project = new { id = context.ProjectId, name = context.ProjectName },
            alert = new
            {
                ruleCode = context.Alert.RuleCode,
                message = context.Alert.Message,
                severity = context.Alert.Severity
            }
        }, JsonOptions);

        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, syntax.Uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("User-Agent", "ApiInsightStudio-Webhook/1.0");
        request.Headers.TryAddWithoutValidation("X-Event-Id", context.Alert.EventId.ToString());
        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("X-Signature", WebhookSigner.HeaderValue(secret, timestamp, body));

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? DeliveryResult.Ok(code)
                : DeliveryResult.Failed(code, $"Alıcı {code} durum kodu döndürdü.");
        }
        catch (HttpRequestException ex) when (FindUnsafeTarget(ex) is not null)
        {
            return DeliveryResult.Permanent(FindUnsafeTarget(ex)!.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DeliveryResult.Failed(null, "Zaman aşımı: alıcı 5 saniyede cevap vermedi.");
        }
        catch (HttpRequestException ex)
        {
            // Adres hata metnine konmaz: sorgu parametrelerinde token olabilir.
            return DeliveryResult.Failed(null, $"Bağlantı hatası: {ex.GetBaseException().GetType().Name}.");
        }
    }

    private static UnsafeTargetException? FindUnsafeTarget(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnsafeTargetException unsafeTarget)
                return unsafeTarget;
        }

        return null;
    }
}
