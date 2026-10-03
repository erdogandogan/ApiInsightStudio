using System.Net;
using ApiInsightStudio.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace ApiInsightStudio.Api.Tests;

/// <summary>
/// Telegram yapılandırılmış (sahte token/sohbet) ve Telegram'a giden istekleri gerçek ağa çıkmadan yakalayan fabrika.
/// Sahte token gerçek bir token değildir; testlerde günlüklere/cevaplara sızmadığı kontrol edilir.
/// </summary>
public sealed class TelegramApiFactory : ApiFactory
{
    public const string FakeToken = "123456789:TEST-ASLA-GERCEK-DEGIL-abcdefghijklmnop";
    public const string FakeChatId = "424242";

    public sealed record CapturedTelegramRequest(string PathAndQuery, string Body);

    private readonly List<CapturedTelegramRequest> _requests = new();

    /// <summary>Telegram'a gönderilmek istenen isteklerin kopyası.</summary>
    public IReadOnlyList<CapturedTelegramRequest> TelegramRequests
    {
        get { lock (_requests) return _requests.ToList(); }
    }

    /// <summary>Sonraki isteklere verilecek durum kodu ve gövde; varsayılan başarılı cevap.</summary>
    public HttpStatusCode ReplyStatus { get; set; } = HttpStatusCode.OK;
    public string ReplyBody { get; set; } = "{\"ok\":true,\"result\":{\"message_id\":1}}";

    protected override IReadOnlyDictionary<string, string> ExtraSettings { get; } = new Dictionary<string, string>
    {
        ["Telegram:BotToken"] = FakeToken,
        ["Telegram:ChatId"] = FakeChatId
    };

    protected override void ConfigureExtraServices(IServiceCollection services)
    {
        services.AddHttpClient(TelegramChannel.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(this));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly TelegramApiFactory _owner;

        public CapturingHandler(TelegramApiFactory owner) => _owner = owner;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_owner._requests)
                _owner._requests.Add(new CapturedTelegramRequest(request.RequestUri!.PathAndQuery, body));

            return new HttpResponseMessage(_owner.ReplyStatus) { Content = new StringContent(_owner.ReplyBody) };
        }
    }
}
