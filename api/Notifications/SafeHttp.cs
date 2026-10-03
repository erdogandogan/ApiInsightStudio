using System.Net;
using System.Net.Sockets;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Bildirim isteklerinde kullanılan güvenli HTTP işleyicisi. DNS çözümü ve IP doğrulaması, bağlantının
/// kurulduğu anda ve bağlanılacak IP üzerinde yapılır; böylece kayıt anında güvenli görünen bir ad
/// sonradan iç ağa çözülse bile (DNS rebinding) bağlantı engellenir.
/// </summary>
public static class SafeHttp
{
    public static SocketsHttpHandler CreateHandler(IReadOnlyCollection<string> allowedPrivateHosts) => new()
    {
        AllowAutoRedirect = false,   // yönlendirme, güvenli adresten iç ağa sıçrama yolu olabilir
        UseProxy = false,            // proxy, bağlantı denetimini atlatır
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var host = context.DnsEndPoint.Host;
            var addresses = await UrlSafety.ResolveAsync(host, cancellationToken);

            var hostAllowed = UrlSafety.IsAllowedHost(host, allowedPrivateHosts);
            var usable = addresses.Where(address => hostAllowed || UrlSafety.IsPublicAddress(address)).ToArray();
            if (usable.Length == 0)
                throw new UnsafeTargetException("Hedef adres özel/yerel bir ağa çözülüyor; bağlantı engellendi.");

            Exception? last = null;
            foreach (var address in usable)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    socket.Dispose();
                    last = ex;
                }
            }

            throw new HttpRequestException("Hedefe bağlantı kurulamadı.", last);
        }
    };
}
