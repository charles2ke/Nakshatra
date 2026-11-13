using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

/// <summary>Applies a status transition and streams the change to Kafka.</summary>
public static class OrderStatusUpdater
{
    public static async Task<Order> ApplyAsync(
        IDocumentRepository<Order> repo,
        IEventPublisher publisher,
        Order order,
        OrderStatus status,
        string note,
        CancellationToken ct = default)
    {
        if (order.Status != status)
        {
            order.Status = status;
            order.History.Add(new OrderStatusChange { Status = status, Note = note });
            await repo.UpsertAsync(order, ct);
            await publisher.PublishAsync(Topics.OrdersStatusChanged, order.Id, order, ct);
        }

        return order;
    }
}

/// <summary>
/// Moves an order to <see cref="OrderStatus.Paid"/> when the payment service reports a captured
/// payment on the <c>payments.completed</c> stream, and cancels it when the payment is refunded.
/// </summary>
public class PaymentStreamConsumer : EventConsumerBackgroundService
{
    public PaymentStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<PaymentStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.PaymentsCompleted;

    protected override string ConsumerGroup => "order-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var payment = envelope.As<Payment>();
        if (payment is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Order>>();
        var publisher = scopedServices.GetRequiredService<IEventPublisher>();
        var order = await repo.GetAsync(payment.OrderId, ct);
        if (order is null)
        {
            return;
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            var catalog = scopedServices.GetRequiredService<CatalogClient>();
            await OrderCancellation.CancelAsync(repo, publisher, catalog, order, $"Payment {payment.Id} refunded.", ct);
            return;
        }

        // Late or duplicate payment events must not move an order backwards, e.g. a failed retry
        // arriving after a successful capture, or any payment event for a cancelled order.
        if (order.Status is not (OrderStatus.Created or OrderStatus.AwaitingPayment))
        {
            return;
        }

        var (status, note) = payment.Status switch
        {
            PaymentStatus.Captured => (OrderStatus.Paid, $"Payment {payment.Id} captured."),
            PaymentStatus.Authorized => (OrderStatus.AwaitingPayment, $"Payment {payment.Id} authorized."),
            _ => (OrderStatus.AwaitingPayment, $"Payment {payment.Id} failed: {payment.FailureReason}")
        };

        await OrderStatusUpdater.ApplyAsync(repo, publisher, order, status, note, ct);
    }
}
