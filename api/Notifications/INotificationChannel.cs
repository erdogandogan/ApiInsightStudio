using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>Bir kanalın gönderim için ihtiyaç duyduğu bağlam.</summary>
public sealed record NotificationContext(int ProjectId, string ProjectName, AlertRaised Alert, ProjectAutomationSettings Settings);

/// <summary>Bir gönderim denemesinin sonucu.</summary>
public sealed record DeliveryResult(bool Success, int? StatusCode, string? Error, bool IsPermanent)
{
    public static DeliveryResult Ok(int? statusCode = null) => new(true, statusCode, null, false);

    /// <summary>Geçici hata: bekleyip yeniden denenir.</summary>
    public static DeliveryResult Failed(int? statusCode, string error) => new(false, statusCode, error, false);

    /// <summary>Kalıcı hata (yapılandırma eksik/güvensiz): yeniden denemenin anlamı yok, hemen ölü sayılır.</summary>
    public static DeliveryResult Permanent(string error) => new(false, null, error, true);
}

/// <summary>Bir bildirim kanalı (webhook, Telegram, ileride Slack/e-posta).</summary>
public interface INotificationChannel
{
    /// <summary>Kanalın benzersiz adı; <see cref="NotificationDelivery.Channel"/> alanında saklanır.</summary>
    string Name { get; }

    Task<DeliveryResult> SendAsync(NotificationContext context, CancellationToken cancellationToken);
}
