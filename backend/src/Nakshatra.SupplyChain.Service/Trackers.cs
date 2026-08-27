using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.SupplyChain.Service;

/// <summary>
/// Appends an event to a trace, keeps the current stage in sync and raises a notification request
/// so the customer (or vendor) is told what happened.
/// </summary>
public static class TraceRecorder
{
    public static async Task<SupplyChainTrace> RecordAsync(
        IDocumentRepository<SupplyChainTrace> repo,
        IEventPublisher publisher,
        string reference,
        string referenceType,
        string userId,
        SupplyChainStage stage,
        string description,
        string location,
        string actor,
        DateTime? estimatedDelivery = null,
        CancellationToken ct = default)
    {
        var trace = await repo.GetAsync(reference, ct) ?? new SupplyChainTrace
        {
            Id = reference,
            Reference = reference,
            ReferenceType = referenceType
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            trace.UserId = userId;
        }

        var alreadyRecorded = trace.Events.Any(e => e.Stage == stage && e.Description == description);
        if (!alreadyRecorded)
        {
            trace.Events.Add(new SupplyChainEvent
            {
                Stage = stage,
                Description = description,
                Location = location,
                Actor = actor
            });
        }

        trace.CurrentStage = stage;
        trace.EstimatedDelivery = estimatedDelivery ?? trace.EstimatedDelivery;
        trace.UpdatedAt = DateTime.UtcNow;
        await repo.UpsertAsync(trace, ct);

        if (!alreadyRecorded)
        {
            await publisher.PublishAsync(Topics.SupplyChainEventRecorded, reference, new
            {
                reference,
                referenceType,
                userId = trace.UserId,
                stage = stage.ToString(),
                description,
                location,
                estimatedDelivery = trace.EstimatedDelivery
            }, ct);
        }

        return trace;
    }
}

/// <summary>Starts a trace when an order is created.</summary>
public class OrderCreatedTracker : EventConsumerBackgroundService
{
    public OrderCreatedTracker(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderCreatedTracker> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "supplychain-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null)
        {
            return;
        }

        await TraceRecorder.RecordAsync(
            scopedServices.GetRequiredService<IDocumentRepository<SupplyChainTrace>>(),
            scopedServices.GetRequiredService<IEventPublisher>(),
            order.Id,
            "order",
            order.UserId,
            SupplyChainStage.OrderPlaced,
            $"Order placed with {order.Items.Count} line(s).",
            "nakshatra.com",
            "customer",
            DateTime.UtcNow.AddDays(5),
            ct);
    }
}

/// <summary>Maps order status changes onto supply chain stages.</summary>
public class OrderStatusTracker : EventConsumerBackgroundService
{
    public OrderStatusTracker(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStatusTracker> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersStatusChanged;

    protected override string ConsumerGroup => "supplychain-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null)
        {
            return;
        }

        var (stage, description, location) = order.Status switch
        {
            OrderStatus.Paid => (SupplyChainStage.PaymentSettled, "Payment settled.", "payments"),
            OrderStatus.Packed => (SupplyChainStage.Packed, "Items picked and packed.", "fulfillment centre"),
            OrderStatus.Shipped => (SupplyChainStage.HandedToCarrier, "Handed to the carrier.", "carrier hub"),
            OrderStatus.Delivered => (SupplyChainStage.Delivered, "Delivered to the customer.", "destination"),
            OrderStatus.Cancelled => (SupplyChainStage.Exception, "Order cancelled.", "nakshatra.com"),
            _ => (SupplyChainStage.OrderPlaced, "Order updated.", "nakshatra.com")
        };

        await TraceRecorder.RecordAsync(
            scopedServices.GetRequiredService<IDocumentRepository<SupplyChainTrace>>(),
            scopedServices.GetRequiredService<IEventPublisher>(),
            order.Id,
            "order",
            order.UserId,
            stage,
            description,
            location,
            "fulfillment",
            ct: ct);
    }
}

/// <summary>Tracks inbound replenishment so the upstream half of the chain is visible too.</summary>
public class ReplenishmentTracker : EventConsumerBackgroundService
{
    public ReplenishmentTracker(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<ReplenishmentTracker> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.InventoryReplenishmentOrdered;

    protected override string ConsumerGroup => "supplychain-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var replenishment = envelope.As<ReplenishmentEvent>();
        if (replenishment is null || string.IsNullOrWhiteSpace(replenishment.ProductId))
        {
            return;
        }

        await TraceRecorder.RecordAsync(
            scopedServices.GetRequiredService<IDocumentRepository<SupplyChainTrace>>(),
            scopedServices.GetRequiredService<IEventPublisher>(),
            $"replenishment-{replenishment.ProductId}",
            "replenishment",
            replenishment.VendorId ?? string.Empty,
            SupplyChainStage.ReplenishmentOrdered,
            $"Replenishment of {replenishment.Quantity} unit(s) ordered from the vendor.",
            "vendor",
            "inventory",
            replenishment.ExpectedArrival,
            ct);
    }
}

public record ReplenishmentEvent(string ProductId, int Quantity, string? VendorId, DateTime? ExpectedArrival);
