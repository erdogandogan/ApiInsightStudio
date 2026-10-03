using System.Net;
using System.Text.Json;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using ApiInsightStudio.Api.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>Telegram kanalı: istek biçimi, hata sınıflandırması ve token'ın hiçbir yere sızmaması.</summary>
public class TelegramChannelTests
{
    private const string Token = "123456789:TEST-ASLA-GERCEK-DEGIL-abcdefghijklmnop";
    private const string ChatId = "424242";

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public int Calls { get; private set; }

        public CapturingHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond();
        }
    }

    private static TelegramChannel CreateChannel(CapturingHandler handler, string? token = Token, string? chatId = ChatId) =>
        new(new FakeHttpClientFactory(handler),
            Options.Create(new TelegramOptions { BotToken = token, ChatId = chatId }));

    private static NotificationContext Context(string message = "Kalite skoru 50, eşik değer (60) altında.") =>
        new(7, "Proje Adı",
            new AlertRaised(7, "LOW_SCORE", message, "Medium", DateTime.UtcNow, Guid.NewGuid()),
            new ProjectAutomationSettings { ProjectId = 7, NotifyTelegram = true });

    private static HttpResponseMessage Reply(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body) };

    private static HttpResponseMessage Ok() => Reply(HttpStatusCode.OK, "{\"ok\":true,\"result\":{\"message_id\":1}}");

    // ----- İstek biçimi -----

    [Fact]
    public async Task sendMessage_adresine_token_ile_POST_eder_ve_basari_doner()
    {
        var handler = new CapturingHandler(Ok);

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Govde_sohbet_kimligini_ve_dolu_metni_tasir_biçimlendirme_modu_kullanmaz()
    {
        var handler = new CapturingHandler(Ok);

        await CreateChannel(handler).SendAsync(Context("<b>Merhaba</b> *kalın* _eğik_"), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        var root = body.RootElement;
        Assert.Equal(ChatId, root.GetProperty("chat_id").GetString());
        Assert.True(root.GetProperty("disable_web_page_preview").GetBoolean());
        Assert.False(root.TryGetProperty("parse_mode", out _)); // HTML/Markdown enjeksiyonu yok: düz metin
        var text = root.GetProperty("text").GetString()!;
        Assert.Contains("ApiInsightStudio uyarısı", text);
        Assert.Contains("Proje: Proje Adı", text);
        Assert.Contains("LOW_SCORE (Medium)", text);
        Assert.Contains("<b>Merhaba</b> *kalın* _eğik_", text); // olduğu gibi, işlenmeden
    }

    [Fact]
    public async Task Govdede_token_bulunmaz()
    {
        var handler = new CapturingHandler(Ok);

        await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.DoesNotContain(Token, handler.Body);
    }

    [Fact]
    public async Task Cok_uzun_mesaj_Telegram_sinirina_kirpilir()
    {
        var handler = new CapturingHandler(Ok);

        await CreateChannel(handler).SendAsync(Context(new string('x', 10000)), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.True(body.RootElement.GetProperty("text").GetString()!.Length <= 4096);
    }

    // ----- Sonuç sınıflandırma -----

    [Fact]
    public async Task HTTP_200_ama_ok_false_basarisiz_sayilir()
    {
        var handler = new CapturingHandler(() => Reply(HttpStatusCode.OK, "{\"ok\":false,\"description\":\"bir sorun\"}"));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]                         // token yanlış
    [InlineData(HttpStatusCode.Forbidden, "Forbidden: bot was blocked by the user")]  // bot engellenmiş
    [InlineData(HttpStatusCode.BadRequest, "Bad Request: chat not found")]            // sohbet kimliği yanlış
    [InlineData(HttpStatusCode.NotFound, "Not Found")]
    public async Task Yapilandirma_hatalari_kalici_hatadir_ve_aciklamayi_tasir(HttpStatusCode status, string description)
    {
        var body = JsonSerializer.Serialize(new { ok = false, error_code = (int)status, description });
        var handler = new CapturingHandler(() => Reply(status, body));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.IsPermanent);
        Assert.Equal((int)status, result.StatusCode);
        Assert.Contains(description, result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Hiz_siniri_ve_sunucu_hatalari_gecici_hatadir(HttpStatusCode status)
    {
        var handler = new CapturingHandler(() => Reply(status, "{\"ok\":false,\"description\":\"Too Many Requests: retry after 5\"}"));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.Equal((int)status, result.StatusCode);
    }

    [Fact]
    public async Task Gecersiz_JSON_cevap_basarisiz_sayilir_ve_cokmez()
    {
        var handler = new CapturingHandler(() => Reply(HttpStatusCode.OK, "<html>bu json degil</html>"));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Baglanti_hatasi_gecicidir_ve_istisna_metnindeki_tokeni_sizdirmaz()
    {
        var handler = new CapturingHandler(() => throw new HttpRequestException(
            $"https://api.telegram.org/bot{Token}/sendMessage adresine baglanilamadi",
            new System.Net.Sockets.SocketException()));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.IsPermanent);
        Assert.DoesNotContain(Token, result.Error);
        Assert.DoesNotContain("TEST-ASLA", result.Error);
    }

    [Fact]
    public async Task Zaman_asimi_gecici_hatadir()
    {
        var handler = new CapturingHandler(() => throw new TaskCanceledException("zaman aşımı"));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.False(result.IsPermanent);
        Assert.Contains("Zaman aşımı", result.Error);
    }

    [Fact]
    public async Task Cevap_aciklamasi_tokeni_iceriyorsa_gizlenir()
    {
        // Telegram bunu yapmaz, ama savunma amaçlı: hata metni hiçbir koşulda token taşımamalı
        var body = JsonSerializer.Serialize(new { ok = false, description = $"hata: bot{Token} gecersiz" });
        var handler = new CapturingHandler(() => Reply(HttpStatusCode.Unauthorized, body));

        var result = await CreateChannel(handler).SendAsync(Context(), CancellationToken.None);

        Assert.DoesNotContain(Token, result.Error);
        Assert.Contains("***", result.Error);
    }

    [Theory]
    [InlineData("bosluk iceren/gecersiz token")]
    [InlineData("123456789:abc/../../getMe")]      // yol manipülasyonu
    [InlineData("123456789:abcdefghij?x=1")]       // sorgu dizesi
    [InlineData("tokenbicimindedegil")]
    [InlineData("555555555:")]
    public async Task Gecersiz_token_bicimi_kalici_hata_olur_istek_atilmaz_ve_metne_sizmaz(string token)
    {
        var handler = new CapturingHandler(Ok);

        var result = await CreateChannel(handler, token: token).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Equal(0, handler.Calls);
        Assert.DoesNotContain(token, result.Error);
    }

    [Theory]
    [InlineData("424242")]
    [InlineData("-1001234567890")]   // grup/kanal
    [InlineData("@kanaladi")]
    public async Task Gecerli_sohbet_kimligi_bicimleri_kabul_edilir(string chatId)
    {
        var handler = new CapturingHandler(Ok);

        var result = await CreateChannel(handler, chatId: chatId).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("@ab")]
    [InlineData("1; DROP TABLE")]
    public async Task Gecersiz_sohbet_kimligi_kalici_hata_olur_ve_istek_atilmaz(string chatId)
    {
        var handler = new CapturingHandler(Ok);

        var result = await CreateChannel(handler, chatId: chatId).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Equal(0, handler.Calls);
    }

    // ----- Yapılandırma -----

    [Theory]
    [InlineData(null, "42")]
    [InlineData("", "42")]
    [InlineData("   ", "42")]
    [InlineData(Token, null)]
    [InlineData(Token, "")]
    [InlineData(null, null)]
    public async Task Token_veya_sohbet_kimligi_yoksa_kalici_hata_ve_istek_atilmaz(string? token, string? chatId)
    {
        var handler = new CapturingHandler(Ok);

        var result = await CreateChannel(handler, token, chatId).SendAsync(Context(), CancellationToken.None);

        Assert.True(result.IsPermanent);
        Assert.Equal(0, handler.Calls);
        Assert.Contains("yapılandırılmamış", result.Error);
    }

    [Theory]
    [InlineData(Token, "42", true)]
    [InlineData(null, "42", false)]
    [InlineData(Token, null, false)]
    [InlineData("", "", false)]
    public void IsConfigured_ikisi_de_dolu_olmali(string? token, string? chatId, bool expected)
    {
        Assert.Equal(expected, new TelegramOptions { BotToken = token, ChatId = chatId }.IsConfigured);
    }

    // ----- Günlük sızıntısı: gerçek IHttpClientFactory hattı -----

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _owner;

            public CapturingLogger(CapturingLoggerProvider owner) => _owner = owner;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_owner.Messages)
                    _owner.Messages.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
            }
        }
    }

    [Fact]
    public async Task Gercek_HttpClientFactory_hatti_token_iceren_adresi_gunluge_yazmaz()
    {
        var logs = new CapturingLoggerProvider();
        var handler = new CapturingHandler(Ok);

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logs);
        });
        services.Configure<TelegramOptions>(o => { o.BotToken = Token; o.ChatId = ChatId; });
        services.AddTelegramChannel();
        // Gerçek ağa çıkmamak için yalnızca birincil işleyiciyi sahtesiyle değiştir; günlük hattı aynı kalır
        services.AddHttpClient(TelegramChannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var channel = scope.ServiceProvider.GetServices<INotificationChannel>().Single(c => c.Name == "telegram");

        var result = await channel.SendAsync(Context(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, handler.Calls);
        lock (logs.Messages)
        {
            Assert.DoesNotContain(logs.Messages, m => m.Contains(Token) || m.Contains("TEST-ASLA"));
            Assert.DoesNotContain(logs.Messages, m => m.Contains("api.telegram.org/bot"));
        }
    }

    [Fact]
    public async Task Kontrol_testi_filtre_olmadan_ayni_hat_adresi_gunluge_yazar()
    {
        // Koruma gerçekten bir şeyi engelliyor mu? Filtre kaldırılırsa varsayılan günlük adresi (token'ı) yazıyor olmalı.
        var logs = new CapturingLoggerProvider();
        var handler = new CapturingHandler(Ok);

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(logs);
        });
        services.Configure<TelegramOptions>(o => { o.BotToken = Token; o.ChatId = ChatId; });
        services.AddHttpClient(TelegramChannel.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler); // AddTelegramChannel çağrılmadı: filtre yok
        services.AddScoped<INotificationChannel, TelegramChannel>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var channel = scope.ServiceProvider.GetServices<INotificationChannel>().Single();

        await channel.SendAsync(Context(), CancellationToken.None);

        lock (logs.Messages)
            Assert.Contains(logs.Messages, m => m.Contains("api.telegram.org/bot"));
    }
}
