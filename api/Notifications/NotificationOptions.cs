namespace ApiInsightStudio.Api.Notifications;

/// <summary>"Notifications" yapılandırma bölümü.</summary>
public class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>
    /// Özel/yerel ağ adreslerine (örn. localhost) gitmesine bilerek izin verilen ana bilgisayar adları.
    /// Varsayılan boştur; yalnızca geliştirme için kullanılmalıdır (SSRF koruması bu adlar için gevşer).
    /// </summary>
    public string[] AllowedPrivateHosts { get; set; } = Array.Empty<string>();

    /// <summary>Arka plan işçisi açık mı? Testlerde kapatılır.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>İşçinin ne sıklıkla (saniye) bekleyen işleri kontrol edeceği.</summary>
    public int PollSeconds { get; set; } = 5;
}
