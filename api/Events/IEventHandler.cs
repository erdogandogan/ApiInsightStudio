namespace ApiInsightStudio.Api.Events;

/// <summary>
/// Bir olay türünü işler. İşleyici kendi başına SaveChanges çağırmaz: değişiklikleri
/// OutboxDispatcher, "işlendi" işaretiyle aynı işlemde kaydeder.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}
