using System.Security.Cryptography;
using System.Text;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Webhook imzası: HMAC-SHA256(sır, "{zamanDamgası}.{gövde}"), küçük harfli onaltılık. Zaman damgası imzaya
/// dahil olduğu için alıcı, eski bir isteğin tekrar oynatılmasını (replay) reddedebilir.
/// </summary>
public static class WebhookSigner
{
    public static string Sign(string secret, long unixTimestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes($"{unixTimestamp}.{body}");
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }

    /// <summary>X-Signature başlığının değeri.</summary>
    public static string HeaderValue(string secret, long unixTimestamp, string body) =>
        "sha256=" + Sign(secret, unixTimestamp, body);
}
