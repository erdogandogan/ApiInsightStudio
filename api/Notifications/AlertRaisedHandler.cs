using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Events;
using ApiInsightStudio.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiInsightStudio.Api.Notifications;

/// <summary>
/// Yeni bir uyarı açılınca, projede yapılandırılmış her kanal için bir teslimat satırı oluşturur.
/// Gerçek gönderim burada değil, arka plandaki <see cref="DeliveryProcessor"/> tarafından yapılır;
/// böylece yavaş veya çöken bir alıcı analiz isteğini ya da olay işlemeyi bekletmez.
/// </summary>
public class AlertRaisedHandler : IEventHandler<AlertRaised>
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _time;

    public AlertRaisedHandler(AppDbContext dbContext, TimeProvider time)
    {
        _dbContext = dbContext;
        _time = time;
    }

    public async Task HandleAsync(AlertRaised @event, CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ProjectAutomationSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId, cancellationToken);

        // Proje silinmiş ya da ayar yoksa gönderilecek kanal da yoktur.
        if (settings is null)
            return;

        var channels = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.WebhookUrl))
            channels.Add(WebhookChannel.ChannelName);
        if (settings.NotifyTelegram)
            channels.Add(TelegramChannel.ChannelName);

        var payload = JsonSerializer.Serialize(@event);
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var channel in channels)
        {
            // Aynı olay ve kanal için ikinci bir satır açılmaz (idempotency)
            var exists = await _dbContext.NotificationDeliveries
                .AnyAsync(d => d.EventId == @event.EventId && d.Channel == channel, cancellationToken);
            if (exists)
                continue;

            _dbContext.NotificationDeliveries.Add(new NotificationDelivery
            {
                ProjectId = @event.ProjectId,
                EventId = @event.EventId,
                Channel = channel,
                Status = NotificationDelivery.StatusPending,
                PayloadJson = payload,
                CreatedAt = now
            });
        }
    }
}
