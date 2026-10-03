using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace ApiInsightStudio.Api.TestRunner;

/// <summary>
/// Yol şablonundaki parametreleri ({id} gibi) "var olmayan kaynak" için tipine uygun sahte değerlerle doldurur:
/// tamsayı → 0, uuid → sıfırlı uuid, date → 1970-01-01, boolean → false, diğer dizgi → "nonexistent".
/// Tip bilinmiyorsa "0" kullanılır. Böylece sunucu 400 (geçersiz biçim) yerine 404 (bulunamadı) dönebilir.
/// </summary>
public sealed class OpenApiPathParameterFiller
{
    private static readonly Regex Placeholder = new(@"\{([^{}/]+)\}", RegexOptions.Compiled);

    private readonly Dictionary<(string Path, string Method), Dictionary<string, (string? Type, string? Format)>> _parameters;

    private OpenApiPathParameterFiller(
        Dictionary<(string, string), Dictionary<string, (string?, string?)>> parameters)
    {
        _parameters = parameters;
    }

    /// <summary>Tip bilgisi olmadan, tüm parametreleri "0" ile dolduran doldurucu.</summary>
    public static OpenApiPathParameterFiller Empty { get; } = new(new());

    /// <summary>OpenAPI dokümanından parametre tiplerini okur. Doküman okunamazsa boş doldurucu döner (asla istisna atmaz).</summary>
    public static OpenApiPathParameterFiller FromOpenApi(string? openApiContent)
    {
        if (string.IsNullOrWhiteSpace(openApiContent))
            return Empty;

        try
        {
            var document = new OpenApiStringReader().Read(openApiContent, out _);
            if (document?.Paths is null)
                return Empty;

            var map = new Dictionary<(string, string), Dictionary<string, (string?, string?)>>();
            foreach (var (path, item) in document.Paths)
            {
                foreach (var (operationType, operation) in item.Operations)
                {
                    var byName = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
                    foreach (var parameter in item.Parameters.Concat(operation.Parameters))
                    {
                        if (parameter.In == ParameterLocation.Path && !string.IsNullOrEmpty(parameter.Name))
                            byName[parameter.Name] = (parameter.Schema?.Type, parameter.Schema?.Format);
                    }

                    map[(path, operationType.ToString().ToUpperInvariant())] = byName;
                }
            }

            return new OpenApiPathParameterFiller(map);
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    public string Fill(string method, string pathTemplate)
    {
        _parameters.TryGetValue((pathTemplate, method.ToUpperInvariant()), out var known);

        return Placeholder.Replace(pathTemplate, match =>
        {
            var name = match.Groups[1].Value;
            (string? Type, string? Format) info = default;
            known?.TryGetValue(name, out info);
            return Uri.EscapeDataString(ValueFor(info.Type, info.Format));
        });
    }

    private static string ValueFor(string? type, string? format)
    {
        return (type, format?.ToLowerInvariant()) switch
        {
            ("integer", _) or ("number", _) => "0",
            ("boolean", _) => "false",
            ("string", "uuid" or "guid") => "00000000-0000-0000-0000-000000000000",
            ("string", "date") => "1970-01-01",
            ("string", "date-time") => "1970-01-01T00:00:00Z",
            ("string", _) => "nonexistent",
            _ => "0"
        };
    }
}
