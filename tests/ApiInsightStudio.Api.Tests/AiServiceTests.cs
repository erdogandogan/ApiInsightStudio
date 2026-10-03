using System.Net;
using System.Text.Json;
using ApiInsightStudio.Api.Services;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Tests;

/// <summary>AiService'in OpenAI uyumlu isteği doğru kurduğunu ve cevabı doğru işlediğini doğrular.</summary>
public class AiServiceTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        public CapturingHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond();
        }
    }

    private static HttpResponseMessage Reply(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } }
        }))
    };

    private static (AiService Service, CapturingHandler Handler) Create(AiOptions options, Func<HttpResponseMessage> respond)
    {
        var handler = new CapturingHandler(respond);
        return (new AiService(new HttpClient(handler), Options.Create(options)), handler);
    }

    [Fact]
    public async Task Istek_OpenAI_uyumlu_chat_completions_adresine_modelle_gider()
    {
        var (service, handler) = Create(new AiOptions { BaseUrl = "http://localhost:11434/", Model = "qwen2.5:3b" },
            () => Reply("Ürünleri listeler."));

        await service.GenerateDescriptionAsync("GET", "/items");

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request.RequestUri!.ToString());

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("qwen2.5:3b", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        var message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Contains("GET", message.GetProperty("content").GetString());
        Assert.Contains("/items", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Anahtar_yoksa_Authorization_basligi_gonderilmez()
    {
        var (service, handler) = Create(new AiOptions(), () => Reply("x"));

        await service.GenerateDescriptionAsync("GET", "/items");

        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task Anahtar_varsa_Bearer_olarak_gonderilir()
    {
        var (service, handler) = Create(new AiOptions { ApiKey = "test-anahtar" }, () => Reply("x"));

        await service.GenerateDescriptionAsync("GET", "/items");

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("test-anahtar", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Saglayici_ve_model_yapilandirmadan_gelir()
    {
        var (service, handler) = Create(new AiOptions { BaseUrl = "http://localhost:1234", Model = "baska-model" },
            () => Reply("x"));

        await service.GenerateDescriptionAsync("GET", "/items");

        Assert.Equal("http://localhost:1234/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("baska-model", service.Model);
        Assert.Equal("http://localhost:1234", service.BaseUrl);
    }

    [Fact]
    public async Task Cevap_metni_kirpilarak_doner()
    {
        var (service, _) = Create(new AiOptions(), () => Reply("  Ürünleri listeler.\n"));

        Assert.Equal("Ürünleri listeler.", await service.GenerateDescriptionAsync("GET", "/items"));
    }

    [Fact]
    public async Task Hata_durum_kodu_HttpRequestException_olarak_kod_ile_atilir()
    {
        var (service, _) = Create(new AiOptions(), () => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => service.GenerateDescriptionAsync("GET", "/items"));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"  \"}}]}")]
    public async Task Beklenmeyen_veya_bos_cevap_InvalidOperationException_atar(string json)
    {
        var (service, _) = Create(new AiOptions(),
            () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateDescriptionAsync("GET", "/items"));
    }

    [Fact]
    public void Varsayilan_ayarlar_Ollama_ve_qwen_7b_dir()
    {
        var options = new AiOptions();

        Assert.Equal("http://localhost:11434", options.BaseUrl);
        Assert.Equal("qwen2.5:7b", options.Model);
        Assert.Null(options.ApiKey);
    }
}
