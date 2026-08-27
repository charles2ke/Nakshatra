using System.Text.Json;

namespace Nakshatra.Shared.Messaging;

public static class Topics
{
    public const string OrdersCreated = "orders.created";
    public const string OrdersStatusChanged = "orders.status-changed";
    public const string PaymentsCompleted = "payments.completed";
    public const string CartItemAdded = "cart.item-added";
}

public record EventEnvelope(string Topic, string Key, string Payload)
{
    public T? As<T>() => JsonSerializer.Deserialize<T>(Payload);
}

/// <summary>Publishes domain events to the streaming platform (Kafka).</summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(string topic, string key, T payload, CancellationToken ct = default);
}

/// <summary>Consumes domain events from the streaming platform (Kafka).</summary>
public interface IEventSubscriber
{
    /// <summary>
    /// Subscribes to a topic. Kafka implementations block for the lifetime of <paramref name="ct"/>,
    /// so callers should invoke this from a background service.
    /// </summary>
    Task SubscribeAsync(string topic, string consumerGroup, Func<EventEnvelope, CancellationToken, Task> handler, CancellationToken ct);
}
