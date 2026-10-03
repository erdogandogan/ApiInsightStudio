namespace ApiInsightStudio.Api.Services;

/// <summary>
/// AI sağlayıcısı ayarları ("Ai" bölümü). OpenAI uyumlu bir sohbet uç noktası sunan herhangi bir yerel
/// sağlayıcı (Ollama, LM Studio, llama.cpp server, vLLM) yalnızca bu ayarlarla değiştirilebilir.
/// </summary>
public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Sağlayıcının kök adresi, "/v1" olmadan (örn. http://localhost:11434 Ollama, http://localhost:1234 LM Studio).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Kullanılacak model adı (sağlayıcıda yüklü olmalı).</summary>
    public string Model { get; set; } = "qwen2.5:7b";

    /// <summary>İsteğe bağlı: anahtar isteyen sağlayıcılar için. Gizli bilgidir; user-secrets ile verilmeli, repoya yazılmamalı.</summary>
    public string? ApiKey { get; set; }
}
