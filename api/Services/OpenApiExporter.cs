using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApiInsightStudio.Api.Services;

/// <summary>Dışa aktarma sonucu: güncellenmiş doküman ve ne yapıldığının sayıları (metinler değil).</summary>
public sealed record OpenApiExportResult(string Json, int Filled, int SkippedExistingSummary, int NotFoundInDocument);

/// <summary>
/// Yüklenen OpenAPI (JSON) dokümanının bir KOPYASINA, insanın onayladığı AI açıklamalarını <c>summary</c> alanı olarak yazar.
/// Yalnızca açıklaması boş olan işlemlere yazar (yazarın kendi metnine dokunmaz); dokümanın geri kalanı olduğu gibi kalır.
/// Yazılan her işleme <c>x-ai-onayli: true</c> işareti konur, böylece sonradan ayırt edilebilir.
/// </summary>
public static class OpenApiExporter
{
    public const string ApprovedMarker = "x-ai-onayli";

    private static readonly HashSet<string> OperationKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "get", "put", "post", "delete", "options", "head", "patch", "trace"
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Türkçe karakterler ü gibi kaçırılmadan, okunur yazılır
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <param name="openApiJson">Projenin yüklenmiş OpenAPI JSON metni.</param>
    /// <param name="approved">(Metot, yol) → onaylanmış açıklama. Metot büyük/küçük harf duyarsız eşleşir.</param>
    /// <exception cref="JsonException">Doküman geçerli JSON değilse.</exception>
    public static OpenApiExportResult Export(string openApiJson, IEnumerable<(string Method, string Path, string Text)> approved)
    {
        var root = JsonNode.Parse(openApiJson) as JsonObject
                   ?? throw new JsonException("OpenAPI dokümanı bir JSON nesnesi olmalı.");

        var paths = root["paths"] as JsonObject;
        int filled = 0, skipped = 0, notFound = 0;

        foreach (var (method, path, text) in approved)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var operation = FindOperation(paths, method, path);
            if (operation is null)
            {
                notFound++;
                continue;
            }

            // Yükleme ile aynı kural: özet yoksa description özet sayılır; ikisi de boş/boşluksa açıklama eksiktir.
            var existing = ReadString(operation["summary"]) ?? ReadString(operation["description"]);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                skipped++;
                continue;
            }

            operation["summary"] = text.Trim();
            operation[ApprovedMarker] = true;
            filled++;
        }

        return new OpenApiExportResult(root.ToJsonString(WriteOptions), filled, skipped, notFound);
    }

    private static JsonObject? FindOperation(JsonObject? paths, string method, string path)
    {
        if (paths is null || !paths.TryGetPropertyValue(path, out var pathItem) || pathItem is not JsonObject item)
            return null;

        // "parameters", "summary" gibi yol seviyesi anahtarlar işlem değildir; yalnızca HTTP metotları aranır.
        if (!OperationKeys.Contains(method))
            return null;

        foreach (var (key, value) in item)
        {
            if (string.Equals(key, method, StringComparison.OrdinalIgnoreCase))
                return value as JsonObject;
        }

        return null;
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
