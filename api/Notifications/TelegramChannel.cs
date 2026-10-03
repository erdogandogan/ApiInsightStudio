using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Uyarıyı Telegram botu aracılığıyla yapılandırılmış sohbete düz metin olarak gönderir.
/// Token, istek adresinin içinde yer aldığı için hiçbir hata metnine, kayda ya da günlüğe yazılmaz
/// (<see cref="NotificationServiceCollectionExtensions.AddTelegramChannel"/> HttpClient günlüklerini kapatır).
/// </summary>
public class TelegramChannel : INotificationChannel
{
    public const string ChannelName = "telegram";
    public const string HttpClientName = "telegram";

    // Telegram mesaj sınırı 4096 karakterdir
    private const int MaxTextLength = 4000;
    private const int MaxDescriptionLength = 150;

    // Bot token'ı: sayısal bot kimliği, iki nokta, harf/rakam/_/-; sohbet: sayı (grup/kanal eksi olabilir) veya @ad
    private static readonly Regex TokenFormat = new(@"^\d{3,}:[A-Za-z0-9_-]{10,}$", RegexOptions.Compiled);
    private static readonly Regex ChatIdFormat = new(@"^(-?\d{1,20}|@[A-Za-z0-9_]{5,})$", RegexOptions.Compiled);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramOptions _options;

    public TelegramChannel(IHttpClientFactory httpClientFactory, IOptions<TelegramOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string Name => ChannelName;

    public async Task<DeliveryResult> SendAsync(NotificationContext context, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            return DeliveryResult.Permanent("Telegram yapılandırılmamış (Telegram:BotToken ve Telegram:ChatId gerekli).");

        var token = _options.BotToken!.Trim();
        var chatId = _options.ChatId!.Trim();

        // Biçim denetimi: adresin yolunu bozabilecek ("/", "?", boşluk) değerler hiç gönderilmeden reddedilir.
        // Hata metni değeri içermez (token gizlidir).
        if (!TokenFormat.IsMatch(token))
            return DeliveryResult.Permanent("Telegram bot token'ı beklenen biçimde değil (örn. 123456789:AAH...).");
        if (!ChatIdFormat.IsMatch(chatId))
            return DeliveryResult.Permanent("Telegram sohbet kimliği beklenen biçimde değil (sayı veya @kullaniciadi).");

        var payload = JsonSerializer.Serialize(new
        {
            chat_id = chatId,
            text = BuildText(context),
            disable_web_page_preview = true
        });

        try
        {
            // Adres burada kurulur: geçersiz bir token/adres UriFormatException fırlatırsa metni (token içerir) sızmasın.
            var uri = new Uri($"{_options.ApiBaseUrl.TrimEnd('/')}/bot{token}/sendMessage");
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var (ok, description) = ParseReply(body);
            var code = (int)response.StatusCode;

            if (response.IsSuccessStatusCode && ok)
                return DeliveryResult.Ok(code);

            var error = Sanitize($"Telegram {code}: {description ?? response.ReasonPhrase ?? "hata"}", token);

            // Yapılandırma hataları (token/sohbet yanlış, bot engellenmiş) yeniden denemekle düzelmez.
            return response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                ? new DeliveryResult(false, code, error, IsPermanent: true)
                : DeliveryResult.Failed(code, error);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DeliveryResult.Failed(null, "Zaman aşımı: Telegram 10 saniyede cevap vermedi.");
        }
        catch (HttpRequestException ex)
        {
            // İstisna metni adres (dolayısıyla token) içerebilir; yalnızca türü kaydedilir.
            return DeliveryResult.Failed(null, $"Bağlantı hatası: {ex.GetBaseException().GetType().Name}.");
        }
        catch (UriFormatException)
        {
            return DeliveryResult.Permanent("Telegram adresi veya token biçimi geçersiz.");
        }
    }

    private static string BuildText(NotificationContext context)
    {
        var alert = context.Alert;
        var text = new StringBuilder()
            .AppendLine("ApiInsightStudio uyarısı")
            .AppendLine($"Proje: {context.ProjectName}")
            .AppendLine($"Kural: {alert.RuleCode} ({alert.Severity})")
            .AppendLine()
            .Append(alert.Message)
            .ToString();

        return text.Length <= MaxTextLength ? text : text[..MaxTextLength];
    }

    private static (bool Ok, string? Description) ParseReply(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
            var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString()
                : null;
            return (ok, description);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    /// <summary>Hata metninde token geçiyorsa gizler ve metni kısaltır.</summary>
    private static string Sanitize(string text, string token)
    {
        text = text.Replace(token, "***", StringComparison.Ordinal);
        return text.Length <= MaxDescriptionLength + 20 ? text : text[..(MaxDescriptionLength + 20)];
    }
}
