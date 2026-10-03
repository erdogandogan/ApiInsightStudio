using System.Text.Json;
using ApiInsightStudio.Api.Data;
using ApiInsightStudio.Api.Models;

namespace ApiInsightStudio.Api.Events;

public interface IEventPublisher
{
    /// <summary>
    /// Olayı outbox'a ekler ama kaydetmez: çağıranın SaveChanges çağrısı, olayı kendi
    /// değişiklikleriyle aynı işlemde kalıcı hale getirir.
    /// </summary>
    void Publish<TEvent>(TEvent @event) where TEvent : class;
}

public class OutboxEventPublisher : IEventPublisher
{
    private readonly AppDbContext _dbContext;

    public OutboxEventPublisher(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : class
    {
        _dbContext.OutboxMessages.Add(new OutboxMessage
        {
            Type = typeof(TEvent).Name,
            PayloadJson = JsonSerializer.Serialize(@event),
            CreatedAt = DateTime.UtcNow
        });
    }
}
