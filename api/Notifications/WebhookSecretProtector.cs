using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Webhook imza sırlarını üretir ve veritabanında şifreli saklar (ASP.NET Data Protection). Sırrın düz hali
/// yalnızca üretildiği anda kullanıcıya gösterilir, bir daha okunamaz; imza için gönderim anında çözülür.
/// </summary>
public class WebhookSecretProtector
{
    private const string Purpose = "ApiInsightStudio.WebhookSecret.v1";
    private const string Prefix = "whsec_";

    private readonly IDataProtector _protector;

    public WebhookSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Yeni bir rastgele sır üretir (256 bit).</summary>
    public string Generate() => Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
