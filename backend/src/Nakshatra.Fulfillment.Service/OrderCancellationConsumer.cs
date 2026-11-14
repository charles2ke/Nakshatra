using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

/// <summary>
/// Takes a shipment off the fulfillment board when its order is cancelled on
/// <c>orders.status-changed</c>, unless the goods have already been handed to the carrier.
/// </summary>
public class OrderCancellationConsumer : EventConsumerBackgroundService
{
    public OrderCancellationConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderCancellationConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersStatusChanged;

    protected override string ConsumerGroup => "fulfillment-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is not { Status: OrderStatus.Cancelled })
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Shipment>>();
        var shipment = await repo.GetAsync(order.Id, ct) ?? new Shipment
        {
            Id = order.Id,
            OrderId = order.Id,
            TrackingNumber = $"NK{order.Id[..Math.Min(8, order.Id.Length)].ToUpperInvariant()}"
        };
        if (shipment.Status is not (ShipmentStatus.Pending or ShipmentStatus.Packed))
        {
            return;
        }

        shipment.Status = ShipmentStatus.Cancelled;
        shipment.UpdatedAt = DateTime.UtcNow;
        await repo.UpsertAsync(shipment, ct);
    }
}
