using ApiInsightStudio.Api.Notifications;

namespace ApiInsightStudio.Api.TestRunner;

/// <summary>
/// Hedef API'nin kök adresi için kurallar: webhook adresiyle aynı SSRF sözdizimi kuralları (yalnızca https,
/// kullanıcı bilgisi yok, izin listesi dışında özel ağ yok) ve ek olarak sorgu/parça içermez (yollar bunun üstüne eklenir).
/// </summary>
public static class TargetUrl
{
    public static UrlCheck Validate(string? url, IEnumerable<string> allowedPrivateHosts)
    {
        var syntax = UrlSafety.ValidateSyntax(url, allowedPrivateHosts);
        if (!syntax.Ok)
            return syntax;

        if (!string.IsNullOrEmpty(syntax.Uri!.Query) || !string.IsNullOrEmpty(syntax.Uri.Fragment))
            return new UrlCheck(false, "Hedef kök adres sorgu (?) veya parça (#) içeremez.", null);

        return syntax;
    }

    /// <summary>Kayıt anında: sözdizimi + adresin çözüldüğü IP'lerin herkese açık olması (webhook ile aynı).</summary>
    public static async Task<UrlCheck> ValidateAsync(
        string? url, IReadOnlyCollection<string> allowedPrivateHosts, CancellationToken cancellationToken = default)
    {
        var local = Validate(url, allowedPrivateHosts);
        if (!local.Ok)
            return local;

        return await UrlSafety.ValidateAsync(url, allowedPrivateHosts, cancellationToken);
    }

    /// <summary>
    /// Kök adres + istek yolunu birleştirir. Sonuç, kök adresle aynı şema/ana bilgisayar/port ve kök yol
    /// ön ekinde değilse null döner (hedefin dışına çıkan istek atılmaz).
    /// </summary>
    public static Uri? Combine(Uri baseUri, string requestPath)
    {
        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var text = baseUri.GetLeftPart(UriPartial.Authority) + basePath + requestPath;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var combined))
            return null;

        var sameOrigin = combined.Scheme == baseUri.Scheme
                         && string.Equals(combined.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
                         && combined.Port == baseUri.Port;
        var underBasePath = basePath.Length == 0
                            || combined.AbsolutePath.StartsWith(basePath + "/", StringComparison.Ordinal)
                            || combined.AbsolutePath == basePath;

        return sameOrigin && underBasePath ? combined : null;
    }
}
