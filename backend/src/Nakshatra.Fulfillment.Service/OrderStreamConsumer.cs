using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

/// <summary>
/// Creates a shipment record for every order streamed on <c>orders.created</c>.
/// </summary>
public class OrderStreamConsumer : EventConsumerBackgroundService
{
    public OrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "fulfillment-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Shipment>>();
        if (await repo.GetAsync(order.Id, ct) is not null)
        {
            return;
        }

        await repo.UpsertAsync(new Shipment
        {
            Id = order.Id,
            OrderId = order.Id,
            Status = ShipmentStatus.Pending,
            TrackingNumber = $"NK{order.Id[..Math.Min(8, order.Id.Length)].ToUpperInvariant()}"
        }, ct);
    }
}
