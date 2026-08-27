using Nakshatra.Shared.Http;
using Nakshatra.Shared.Messaging;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

/// <summary>
/// Raises a draft invoice as soon as an order is streamed on <c>orders.created</c>.
/// </summary>
public class OrderStreamConsumer : EventConsumerBackgroundService
{
    public OrderStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<OrderStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.OrdersCreated;

    protected override string ConsumerGroup => "billing-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var order = envelope.As<Order>();
        if (order is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Invoice>>();
        await InvoiceFactory.EnsureInvoiceAsync(repo, order, ct);
    }
}

/// <summary>
/// Marks the invoice as paid when the payment service reports a captured payment.
/// </summary>
public class PaymentStreamConsumer : EventConsumerBackgroundService
{
    public PaymentStreamConsumer(IEventSubscriber subscriber, IServiceProvider serviceProvider, ILogger<PaymentStreamConsumer> logger)
        : base(subscriber, serviceProvider, logger)
    {
    }

    protected override string Topic => Topics.PaymentsCompleted;

    protected override string ConsumerGroup => "billing-service";

    protected override async Task HandleAsync(EventEnvelope envelope, IServiceProvider scopedServices, CancellationToken ct)
    {
        var payment = envelope.As<Payment>();
        if (payment is null)
        {
            return;
        }

        var repo = scopedServices.GetRequiredService<IDocumentRepository<Invoice>>();
        var invoice = (await repo.FindAsync(i => i.OrderId == payment.OrderId, ct)).FirstOrDefault();

        if (invoice is null)
        {
            var order = await scopedServices.GetRequiredService<OrderClient>().GetOrderAsync(payment.OrderId, ct);
            if (order is null)
            {
                return;
            }

            invoice = await InvoiceFactory.EnsureInvoiceAsync(repo, order, ct);
        }

        invoice.Paid = payment.Status == PaymentStatus.Captured;
        await repo.UpsertAsync(invoice, ct);
    }
}

public static class InvoiceFactory
{
    public static async Task<Invoice> EnsureInvoiceAsync(IDocumentRepository<Invoice> repo, Order order, CancellationToken ct = default)
    {
        var existing = (await repo.FindAsync(i => i.OrderId == order.Id, ct)).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var invoice = new Invoice
        {
            OrderId = order.Id,
            UserId = order.UserId,
            Currency = order.Currency,
            Subtotal = order.Subtotal,
            Tax = order.Tax,
            Total = order.Total,
            Lines = order.Items
                .Select(i => new InvoiceLine { Description = $"{i.Name} x{i.Quantity}", Amount = i.Price * i.Quantity })
                .Append(new InvoiceLine { Description = "Tax", Amount = order.Tax })
                .ToList()
        };

        await repo.UpsertAsync(invoice, ct);
        return invoice;
    }
}
