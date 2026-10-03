namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// "Telegram" yapılandırma bölümü. BotToken ve ChatId gizli bilgidir: yalnızca <c>dotnet user-secrets</c>
/// (veya ortam değişkeni) ile verilir, appsettings dosyalarına ve repoya yazılmaz.
/// </summary>
public class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>BotFather'ın verdiği bot token'ı (örn. 123456789:AAH...).</summary>
    public string? BotToken { get; set; }

    /// <summary>Bildirimlerin gideceği sohbetin sayısal kimliği.</summary>
    public string? ChatId { get; set; }

    /// <summary>Telegram Bot API kök adresi. Operatör ayarıdır (kullanıcı girdisi değildir); yalnızca testlerde değiştirilir.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>Hem token hem sohbet kimliği verilmişse Telegram kanalı kullanılabilir.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(ChatId);
}
