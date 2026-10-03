using System.Net;
using System.Net.Sockets;

namespace ApiInsightStudio.Api.Notifications;

public sealed record UrlCheck(bool Ok, string? Error, Uri? Uri);

/// <summary>Hedef adres güvenli olmadığı için bağlantı engellendi.</summary>
public sealed class UnsafeTargetException : Exception
{
    public UnsafeTargetException(string message) : base(message) { }
}

/// <summary>
/// SSRF koruması: kullanıcının verdiği bir adrese sunucunun, kendi iç ağına veya bulut metadata
/// uç noktasına (örn. 169.254.169.254) istek atmasını engeller.
/// </summary>
public static class UrlSafety
{
    private const int MaxUrlLength = 2048;

    /// <summary>Adresin sözdizimini kontrol eder (DNS'e bakmaz).</summary>
    public static UrlCheck ValidateSyntax(string? url, IEnumerable<string> allowedPrivateHosts)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Fail("Adres boş olamaz.");
        if (url.Length > MaxUrlLength)
            return Fail($"Adres en fazla {MaxUrlLength} karakter olabilir.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Fail("Geçerli bir adres değil.");
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Fail("Yalnızca http veya https adresleri kullanılabilir.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return Fail("Adreste kullanıcı adı/parola bulunamaz.");
        if (string.IsNullOrEmpty(uri.DnsSafeHost))
            return Fail("Adreste ana bilgisayar adı yok.");

        // Düz http yalnızca özellikle izin verilen (geliştirme) adresleri için kabul edilir.
        if (uri.Scheme == Uri.UriSchemeHttp && !IsAllowedHost(uri.DnsSafeHost, allowedPrivateHosts))
            return Fail("Güvenlik için https gerekli.");

        return new UrlCheck(true, null, uri);
    }

    /// <summary>Sözdizimini ve adresin çözüldüğü IP'lerin herkese açık olduğunu kontrol eder.</summary>
    public static async Task<UrlCheck> ValidateAsync(
        string? url, IReadOnlyCollection<string> allowedPrivateHosts, CancellationToken cancellationToken = default)
    {
        var syntax = ValidateSyntax(url, allowedPrivateHosts);
        if (!syntax.Ok)
            return syntax;

        var host = syntax.Uri!.DnsSafeHost;
        if (IsAllowedHost(host, allowedPrivateHosts))
            return syntax;

        IPAddress[] addresses;
        try
        {
            addresses = await ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return Fail("Adres çözümlenemedi (ana bilgisayar bulunamadı).");
        }

        if (addresses.Length == 0)
            return Fail("Adres çözümlenemedi (ana bilgisayar bulunamadı).");
        if (addresses.Any(address => !IsPublicAddress(address)))
            return Fail("Adres özel/yerel bir ağ adresine çözülüyor; bu adrese bildirim gönderilemez.");

        return syntax;
    }

    public static bool IsAllowedHost(string host, IEnumerable<string> allowedPrivateHosts) =>
        allowedPrivateHosts.Any(allowed => string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>IP herkese açık (yönlendirilebilir) bir adres mi? Özel, yerel, ayrılmış ve çok noktaya yayın adresleri false döner.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicIPv4(bytes[0], bytes[1], bytes[2]);

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any))
            return false;
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
            return false;

        // 6to4 (2002::/16): içindeki IPv4 adresine bak
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
            return IsPublicIPv4(bytes[2], bytes[3], bytes[4]);

        // NAT64 (64:ff9b::/96): içindeki IPv4 adresine bak
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b
            && bytes[4..12].All(b => b == 0))
            return IsPublicIPv4(bytes[12], bytes[13], bytes[14]);

        // IPv4 uyumlu (::/96) adresler
        if (bytes[..12].All(b => b == 0))
            return false;

        // Teredo (2001::/32) ve dokümantasyon (2001:db8::/32)
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00)
            return false;
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
            return false;

        return true;
    }

    private static bool IsPublicIPv4(byte a, byte b, byte c)
    {
        if (a == 0) return false;                                  // 0.0.0.0/8
        if (a == 10) return false;                                 // 10.0.0.0/8 özel
        if (a == 100 && b >= 64 && b <= 127) return false;         // 100.64.0.0/10 CGNAT
        if (a == 127) return false;                                // loopback
        if (a == 169 && b == 254) return false;                    // link-local, bulut metadata
        if (a == 172 && b >= 16 && b <= 31) return false;          // 172.16.0.0/12 özel
        if (a == 192 && b == 0 && c == 0) return false;            // 192.0.0.0/24
        if (a == 192 && b == 0 && c == 2) return false;            // dokümantasyon
        if (a == 192 && b == 168) return false;                    // 192.168.0.0/16 özel
        if (a == 198 && (b == 18 || b == 19)) return false;        // 198.18.0.0/15 test
        if (a == 198 && b == 51 && c == 100) return false;         // dokümantasyon
        if (a == 203 && b == 0 && c == 113) return false;          // dokümantasyon
        if (a >= 224) return false;                                // çok noktaya yayın, ayrılmış, broadcast
        return true;
    }

    /// <summary>Ana bilgisayar adını (veya IP sabitini, ondalık/onaltılık/kısa yazımlar dahil) IP adreslerine çözer.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

    private static UrlCheck Fail(string error) => new(false, error, null);
}
