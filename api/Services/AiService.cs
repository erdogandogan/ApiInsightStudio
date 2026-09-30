using System.Text;
using System.Text.Json;

namespace ApiInsightStudio.Api.Services;

public class AiService
{
    private readonly HttpClient _httpClient;

    public AiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GenerateDescriptionAsync(string method, string path)
    {
        var prompt = $"Sen bir API dokümantasyon yazarısın. HTTP {method} metoduyla çağrılan '{path}' endpointinin amacını Türkçe olarak yalnızca 1 cümleyle açıkla. Kesinlikle yasak: örnek vermek, 'Örneğin' kelimesi, kod bloğu, backtick, URL, HTTP isteği göstermek, ek bilgi eklemek. Yalnızca tek bir açıklama cümlesi yaz ve dur.";

        var requestBody = new { model = "qwen2.5:7b", prompt = prompt, stream = false };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("http://localhost:11434/api/generate", content);
        response.EnsureSuccessStatusCode();

        var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var text = responseJson.RootElement
            .GetProperty("response")
            .GetString();

        return text ?? string.Empty;
    }
}
