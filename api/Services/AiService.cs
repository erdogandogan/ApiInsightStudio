using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ApiInsightStudio.Api.Services;

public class AiService
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public AiService(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <summary>Yapılandırılmış sağlayıcı adresi (hata mesajlarında kullanılır).</summary>
    public string BaseUrl => _options.BaseUrl;

    /// <summary>Yapılandırılmış model adı (hata mesajlarında ve kayıtlarda kullanılır).</summary>
    public string Model => _options.Model;

    /// <summary>OpenAI uyumlu /v1/chat/completions uç noktasından tek cümlelik açıklama ister.</summary>
    public async Task<string> GenerateDescriptionAsync(string method, string path)
    {
        var prompt = $"Sen bir API dokümantasyon yazarısın. HTTP {method} metoduyla çağrılan '{path}' endpointinin amacını Türkçe olarak yalnızca 1 cümleyle açıkla. Kesinlikle yasak: örnek vermek, 'Örneğin' kelimesi, kod bloğu, backtick, URL, HTTP isteği göstermek, ek bilgi eklemek. Yalnızca tek bir açıklama cümlesi yaz ve dur.";

        var requestBody = new
        {
            model = _options.Model,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri())
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        string? text = null;
        if (json.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            text = content.GetString();
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("AI servisi beklenen biçimde veya dolu bir cevap döndürmedi.");

        return text.Trim();
    }

    private Uri BuildUri() => new(_options.BaseUrl.TrimEnd('/') + "/v1/chat/completions");
}
